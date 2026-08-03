using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Suggestion-6: /api/Import, driven over HTTP exactly as the SPA drives it.
//
// The note's own acceptance sketch is the centrepiece test here: import a real
// de-DE statement twice, and the second run must create nothing and move no
// balance. That is not a nicety — re-importing last month's file with this
// month's appended to it is how the feature actually gets used, and a duplicate
// pass would quietly double someone's spending.
[Collection(IntegrationTestCollection.Name)]
public sealed class StatementImportTests
{
    private readonly CustomWebApplicationFactory _factory;

    public StatementImportTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // A German bank export: semicolon-separated, dotted dates, comma decimals,
    // a memo containing a comma, negative for money leaving.
    private const string GermanStatement =
        "Buchungstag;Verwendungszweck;Betrag\n" +
        "01.08.2026;\"REWE MARKT, BERLIN\";-42,50\n" +
        "02.08.2026;GEHALT AUGUST;2.500,00\n" +
        "03.08.2026;BAECKEREI MITTE;-3,20\n";

    private sealed record Fixture(
        HttpClient Client, Guid AccountId, Guid ExpenseCategoryId, Guid IncomeCategoryId, Guid GroceriesId);

    private async Task<Fixture> ArrangeAsync(decimal balance = 1_000m)
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "EUR");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance);
        var expense = await ApiClient.CreateCategoryAsync(client, categoryType: 0, name: "Uncategorized");
        var income = await ApiClient.CreateCategoryAsync(client, categoryType: 1, name: "Other income");
        var groceries = await ApiClient.CreateCategoryAsync(client, categoryType: 0, name: "Groceries");

        return new Fixture(client, accountId, expense, income, groceries);
    }

    private static MultipartFormDataContent Upload(string csv, object? mapping)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "statement.csv");

        if (mapping is not null)
            form.Add(new StringContent(JsonSerializer.Serialize(mapping)), "mapping");

        return form;
    }

    private static object Mapping(Fixture fx, params object[] rules) => new
    {
        accountId = fx.AccountId,
        dateColumn = 0,
        descriptionColumn = 1,
        amountColumn = 2,
        hasHeaderRow = true,
        delimiter = ";",
        culture = "de-DE",
        negativeIsExpense = true,
        defaultExpenseCategoryId = fx.ExpenseCategoryId,
        defaultIncomeCategoryId = fx.IncomeCategoryId,
        rules
    };

    private static async Task<JsonElement> PostAsync(HttpClient client, string url, string csv, object? mapping)
    {
        var response = await client.PostAsync(url, Upload(csv, mapping));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ── Phase one: looking at the file ─────────────────────────────────────

    [Fact]
    public async Task An_unmapped_upload_answers_with_the_columns_and_a_suggestion()
    {
        var fx = await ArrangeAsync();

        var preview = await PostAsync(fx.Client, "/api/Import/preview", GermanStatement, mapping: null);

        Assert.Equal(";", preview.GetProperty("delimiter").GetString());

        var columns = preview.GetProperty("columns").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Equal(["Buchungstag", "Verwendungszweck", "Betrag"], columns);

        // The mapping screen opens with something sensible already selected.
        var suggested = preview.GetProperty("suggested");
        Assert.Equal(0, suggested.GetProperty("dateColumn").GetInt32());
        Assert.Equal(1, suggested.GetProperty("descriptionColumn").GetInt32());
        Assert.Equal(2, suggested.GetProperty("amountColumn").GetInt32());

        // ... but nothing is parsed until the user has confirmed it.
        Assert.Empty(preview.GetProperty("rows").EnumerateArray());
        Assert.Equal(3, preview.GetProperty("totalRows").GetInt32());
    }

    [Fact]
    public async Task A_mapped_preview_shows_direction_amount_and_category_per_row()
    {
        var fx = await ArrangeAsync();

        var preview = await PostAsync(fx.Client, "/api/Import/preview", GermanStatement,
            Mapping(fx, new { contains = "REWE", categoryId = fx.GroceriesId }));

        var rows = preview.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, preview.GetProperty("readyCount").GetInt32());

        // The quoted memo did not shift the amount column.
        Assert.Equal("REWE MARKT, BERLIN", rows[0].GetProperty("title").GetString());
        Assert.Equal(42.50m, rows[0].GetProperty("amount").GetDecimal());
        Assert.True(rows[0].GetProperty("isExpense").GetBoolean());
        // The rule caught it; the other expense falls to the default.
        Assert.Equal("Groceries", rows[0].GetProperty("categoryName").GetString());
        Assert.True(rows[0].GetProperty("categoryFromRule").GetBoolean());

        Assert.Equal(2500.00m, rows[1].GetProperty("amount").GetDecimal());
        Assert.False(rows[1].GetProperty("isExpense").GetBoolean());
        Assert.Equal("Other income", rows[1].GetProperty("categoryName").GetString());

        Assert.Equal("Uncategorized", rows[2].GetProperty("categoryName").GetString());
        Assert.False(rows[2].GetProperty("categoryFromRule").GetBoolean());
    }

    [Fact]
    public async Task Nothing_is_written_by_a_preview()
    {
        var fx = await ArrangeAsync(balance: 1_000m);

        await PostAsync(fx.Client, "/api/Import/preview", GermanStatement, Mapping(fx));

        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    // ── The note's acceptance sketch ───────────────────────────────────────

    [Fact]
    public async Task Importing_the_same_statement_twice_creates_it_once_and_moves_the_balance_once()
    {
        var fx = await ArrangeAsync(balance: 1_000m);

        var first = await PostAsync(fx.Client, "/api/Import/commit", GermanStatement, Mapping(fx));

        Assert.Equal(3, first.GetProperty("imported").GetInt32());
        Assert.Equal(0, first.GetProperty("duplicatesSkipped").GetInt32());
        Assert.Equal(45.70m, first.GetProperty("expenseTotal").GetDecimal());
        Assert.Equal(2500.00m, first.GetProperty("incomeTotal").GetDecimal());

        // 1000 - 42.50 - 3.20 + 2500
        var afterFirst = await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId);
        Assert.Equal(3_454.30m, afterFirst);

        var second = await PostAsync(fx.Client, "/api/Import/commit", GermanStatement, Mapping(fx));

        // The whole point: month two is the same file with rows appended, and the
        // overlap must cost nothing.
        Assert.Equal(0, second.GetProperty("imported").GetInt32());
        Assert.Equal(3, second.GetProperty("duplicatesSkipped").GetInt32());
        Assert.Equal(afterFirst, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task A_second_statement_overlapping_the_first_imports_only_what_is_new()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        await PostAsync(fx.Client, "/api/Import/commit", GermanStatement, Mapping(fx));

        var extended = GermanStatement + "04.08.2026;NETFLIX;-12,99\n";
        var second = await PostAsync(fx.Client, "/api/Import/commit", extended, Mapping(fx));

        Assert.Equal(1, second.GetProperty("imported").GetInt32());
        Assert.Equal(3, second.GetProperty("duplicatesSkipped").GetInt32());
        Assert.Equal(3_441.31m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task A_line_repeated_inside_one_file_is_imported_once()
    {
        var fx = await ArrangeAsync();

        // Not a re-import — the same file containing the row twice. `seen` grows
        // as the file is walked, so this is caught by the same mechanism.
        var doubled = "Buchungstag;Verwendungszweck;Betrag\n" +
                      "01.08.2026;REWE MARKT;-10,00\n" +
                      "01.08.2026;REWE MARKT;-10,00\n";

        var result = await PostAsync(fx.Client, "/api/Import/commit", doubled, Mapping(fx));

        Assert.Equal(1, result.GetProperty("imported").GetInt32());
        Assert.Equal(1, result.GetProperty("duplicatesSkipped").GetInt32());
    }

    [Fact]
    public async Task Two_genuinely_identical_purchases_on_one_day_are_not_both_dropped()
    {
        var fx = await ArrangeAsync();

        // Buying the same coffee twice is real. The duplicate rule cannot tell
        // this from a repeated line, so the memo has to carry the difference —
        // which is what a bank statement actually does (different reference).
        var twoCoffees = "Buchungstag;Verwendungszweck;Betrag\n" +
                         "01.08.2026;COFFEE REF 001;-3,50\n" +
                         "01.08.2026;COFFEE REF 002;-3,50\n";

        var result = await PostAsync(fx.Client, "/api/Import/commit", twoCoffees, Mapping(fx));

        Assert.Equal(2, result.GetProperty("imported").GetInt32());
    }

    // ── Rows that cannot be read ───────────────────────────────────────────

    [Fact]
    public async Task Unreadable_rows_are_reported_by_line_number_and_never_silently_dropped()
    {
        var fx = await ArrangeAsync(balance: 1_000m);

        var messy = "Buchungstag;Verwendungszweck;Betrag\n" +
                    "01.08.2026;GOOD ROW;-10,00\n" +
                    "not-a-date;BAD DATE;-5,00\n" +
                    "03.08.2026;BAD AMOUNT;pending\n" +
                    "04.08.2026;ZERO;0,00\n";

        var result = await PostAsync(fx.Client, "/api/Import/commit", messy, Mapping(fx));

        Assert.Equal(1, result.GetProperty("imported").GetInt32());
        Assert.Equal(3, result.GetProperty("failed").GetInt32());

        var problems = result.GetProperty("problems").EnumerateArray().ToList();
        Assert.Equal(3, problems.Count);
        // Line numbers are the user's coordinate system — they will open the file
        // in a spreadsheet to find these.
        Assert.Equal([3, 4, 5], problems.Select(p => p.GetProperty("rowNumber").GetInt32()).ToList());

        // Only the one good row moved the balance.
        Assert.Equal(990m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task A_file_with_nothing_importable_leaves_the_account_untouched()
    {
        var fx = await ArrangeAsync(balance: 500m);

        var junk = "Buchungstag;Verwendungszweck;Betrag\nnope;nope;nope\n";
        var result = await PostAsync(fx.Client, "/api/Import/commit", junk, Mapping(fx));

        Assert.Equal(0, result.GetProperty("imported").GetInt32());
        Assert.Equal(500m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    // ── Conventions and ownership ──────────────────────────────────────────

    [Fact]
    public async Task An_export_that_states_debits_as_positives_can_be_flipped()
    {
        var fx = await ArrangeAsync(balance: 100m);

        var withdrawals = "Buchungstag;Verwendungszweck;Betrag\n01.08.2026;ATM;25,00\n";
        var mapping = new
        {
            accountId = fx.AccountId,
            dateColumn = 0,
            descriptionColumn = 1,
            amountColumn = 2,
            hasHeaderRow = true,
            delimiter = ";",
            culture = "de-DE",
            negativeIsExpense = false,
            defaultExpenseCategoryId = fx.ExpenseCategoryId,
            defaultIncomeCategoryId = fx.IncomeCategoryId,
            rules = Array.Empty<object>()
        };

        var result = await PostAsync(fx.Client, "/api/Import/commit", withdrawals, mapping);

        Assert.Equal(1, result.GetProperty("imported").GetInt32());
        Assert.Equal(75m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task Leading_preamble_lines_can_be_skipped()
    {
        var fx = await ArrangeAsync();

        var withPreamble = "Kontoauszug 08/2026\nIBAN DE00 0000\n\n" + GermanStatement;
        var mapping = new
        {
            accountId = fx.AccountId,
            dateColumn = 0,
            descriptionColumn = 1,
            amountColumn = 2,
            hasHeaderRow = true,
            skipRows = 2,
            delimiter = ";",
            culture = "de-DE",
            defaultExpenseCategoryId = fx.ExpenseCategoryId,
            defaultIncomeCategoryId = fx.IncomeCategoryId,
            rules = Array.Empty<object>()
        };

        var result = await PostAsync(fx.Client, "/api/Import/commit", withPreamble, mapping);
        Assert.Equal(3, result.GetProperty("imported").GetInt32());
    }

    [Fact]
    public async Task Another_users_account_is_refused()
    {
        var mine = await ArrangeAsync();
        var stranger = await ArrangeAsync();

        var mapping = new
        {
            accountId = stranger.AccountId,
            dateColumn = 0,
            descriptionColumn = 1,
            amountColumn = 2,
            hasHeaderRow = true,
            delimiter = ";",
            culture = "de-DE",
            defaultExpenseCategoryId = mine.ExpenseCategoryId,
            defaultIncomeCategoryId = mine.IncomeCategoryId,
            rules = Array.Empty<object>()
        };

        var response = await mine.Client.PostAsync("/api/Import/commit", Upload(GermanStatement, mapping));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_rule_pointing_at_another_users_category_is_ignored_rather_than_honoured()
    {
        var stranger = await ArrangeAsync();
        var fx = await ArrangeAsync();

        var preview = await PostAsync(fx.Client, "/api/Import/preview", GermanStatement,
            Mapping(fx, new { contains = "REWE", categoryId = stranger.GroceriesId }));

        // Falls back to this user's own default (Fix-8's lesson: a user-supplied
        // id is not a permission).
        var first = preview.GetProperty("rows").EnumerateArray().First();
        Assert.Equal("Uncategorized", first.GetProperty("categoryName").GetString());
        Assert.False(first.GetProperty("categoryFromRule").GetBoolean());
    }

    [Fact]
    public async Task A_default_category_of_the_wrong_axis_is_refused()
    {
        var fx = await ArrangeAsync();

        var mapping = new
        {
            accountId = fx.AccountId,
            dateColumn = 0,
            descriptionColumn = 1,
            amountColumn = 2,
            hasHeaderRow = true,
            delimiter = ";",
            culture = "de-DE",
            // An income category where the expense default belongs: every imported
            // expense would land off the budget axis entirely.
            defaultExpenseCategoryId = fx.IncomeCategoryId,
            defaultIncomeCategoryId = fx.IncomeCategoryId,
            rules = Array.Empty<object>()
        };

        var response = await fx.Client.PostAsync("/api/Import/commit", Upload(GermanStatement, mapping));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Committing_without_a_complete_mapping_is_refused()
    {
        var fx = await ArrangeAsync();

        var response = await fx.Client.PostAsync("/api/Import/commit", Upload(GermanStatement, mapping: null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_requires_authentication()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsync("/api/Import/preview", Upload(GermanStatement, mapping: null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var fx = await ArrangeAsync();

        var response = await fx.Client.PostAsync("/api/Import/preview", Upload(string.Empty, mapping: null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Imported_rows_are_ordinary_expenses_the_rest_of_the_app_can_see()
    {
        var fx = await ArrangeAsync();
        await PostAsync(fx.Client, "/api/Import/commit", GermanStatement, Mapping(fx));

        // An import that produced rows only the import could see would be worse
        // than useless — budgets and reports have to count them.
        var expenses = await fx.Client.GetAsync("/api/Expense");
        expenses.EnsureSuccessStatusCode();
        var body = await expenses.Content.ReadFromJsonAsync<JsonElement>();

        var titles = (body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("items"))
            .EnumerateArray()
            .Select(e => e.GetProperty("title").GetString())
            .ToList();

        Assert.Contains("REWE MARKT, BERLIN", titles);
        Assert.Contains("BAECKEREI MITTE", titles);
    }
}
