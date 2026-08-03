using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Stream R, sentence 2: "net is analysis truth". Budgets, insights, category
// totals and reports count Amount - ExpectedReimbursement, dated at the
// *expense*; the account keeps the gross figure that actually left it.
//
// Every site here is pinned twice, and the first pin matters more than the
// second: an expense with no split at all must produce byte-identical numbers
// to the ones it produced before any of this existed. Splitting a bill is the
// rare case; not splitting one is what almost every user does with almost
// every expense, and none of those numbers is allowed to move by a cent.
//
// The worked example throughout is the acceptance list's: a 60 bill of which
// the user's own share is 20, so ExpectedReimbursement is 40. The account is
// down 60, and every analysis surface reads 20.
//
// The cashflow projection's two sites live in CashflowProjectionTests and the
// weekly digest's in NotificationJobTests, next to the planned-transaction and
// push-subscriber scaffolding those need.
[Collection(IntegrationTestCollection.Name)]
public sealed class ReimbursementAnalysisTests
{
    private readonly CustomWebApplicationFactory _factory;

    public ReimbursementAnalysisTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly FirstOfThisMonth => new(Today.Year, Today.Month, 1);

    // ── Budgets ───────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unsplit_expense_spends_its_whole_amount_against_the_budget()
    {
        var world = await SeedAsync();
        var budgetId = await CreateBudgetAsync(world, 300m, world.Category);

        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: null, date: new DateOnly(2026, 3, 10));
        await CreateExpenseAsync(world, amount: 150m, expectedReimbursement: null, date: new DateOnly(2026, 3, 20));

        var status = await BudgetStatusAsync(world, budgetId, "2026-03");

        // The regression pin: no reimbursement anywhere near this user, so the
        // budget reads exactly what it read before the net expression existed.
        Assert.Equal(210m, SubtotalFor(status, "EUR"));
        Assert.Equal(210m, status.GetProperty("spent").GetDecimal());
        Assert.Equal(90m, status.GetProperty("remaining").GetDecimal());
    }

    [Fact]
    public async Task A_split_expense_spends_only_the_users_share_against_the_budget()
    {
        var world = await SeedAsync();
        var budgetId = await CreateBudgetAsync(world, 300m, world.Category);

        // 60 out of the account, 40 of it other people's.
        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: 40m, date: new DateOnly(2026, 3, 10));

        var status = await BudgetStatusAsync(world, budgetId, "2026-03");

        Assert.Equal(20m, SubtotalFor(status, "EUR"));
        Assert.Equal(280m, status.GetProperty("remaining").GetDecimal());

        // ...and the account is down the whole bill, because that is what left it.
        Assert.Equal(940m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));
    }

    [Fact]
    public async Task Settling_a_receivable_does_not_move_the_budget()
    {
        var world = await SeedAsync();
        var budgetId = await CreateBudgetAsync(world, 300m, world.Category);

        var expenseId = await CreateExpenseAsync(
            world, amount: 60m, expectedReimbursement: 40m, date: new DateOnly(2026, 3, 10));

        var before = await BudgetStatusAsync(world, budgetId, "2026-03");
        Assert.Equal(20m, SubtotalFor(before, "EUR"));

        // The whole point of dating the deduction at the expense: a friend paying
        // up in July is not a March budget event. If it were, every budget in the
        // app would be retroactively editable by somebody else's behaviour.
        await SettleAsync(world, expenseId, world.OtherAccount, 40m);

        var after = await BudgetStatusAsync(world, budgetId, "2026-03");
        Assert.Equal(20m, SubtotalFor(after, "EUR"));
        Assert.Equal(280m, after.GetProperty("remaining").GetDecimal());
    }

    [Fact]
    public async Task The_overall_budget_nets_split_bills_across_every_category()
    {
        var world = await SeedAsync();
        var otherCategory = await ApiClient.CreateCategoryAsync(world.Client, categoryType: 0);
        var overallId = await CreateBudgetAsync(world, 500m, categoryId: null);

        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: 40m, date: new DateOnly(2026, 3, 4));
        await CreateExpenseAsync(
            world, amount: 100m, expectedReimbursement: null, date: new DateOnly(2026, 3, 5), category: otherCategory);

        var status = await BudgetStatusAsync(world, overallId, "2026-03");

        Assert.Equal(120m, SubtotalFor(status, "EUR"));
    }

    // ── Insights ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Insights_for_an_unsplit_month_report_the_whole_amount()
    {
        var world = await SeedAsync();

        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: null, date: FirstOfThisMonth);

        var insights = await InsightsAsync(world);

        Assert.Equal(60m, insights.GetProperty("currentMonthTotal").GetDecimal());
        Assert.Equal(60m, CategoryFor(insights, world.Category).GetProperty("currentMonthTotal").GetDecimal());

        // A user who never splits a bill sees the two new figures sit at zero
        // rather than at null or at some fraction of their spending.
        Assert.Equal(0m, insights.GetProperty("frontedThisMonth").GetDecimal());
        Assert.Equal(0m, insights.GetProperty("stillOpen").GetDecimal());
    }

    [Fact]
    public async Task Insights_count_only_the_users_share_of_a_split_bill()
    {
        var world = await SeedAsync();

        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: 40m, date: FirstOfThisMonth);

        var insights = await InsightsAsync(world);

        Assert.Equal(20m, insights.GetProperty("currentMonthTotal").GetDecimal());
        Assert.Equal(20m, CategoryFor(insights, world.Category).GetProperty("currentMonthTotal").GetDecimal());
        Assert.Equal(40m, insights.GetProperty("frontedThisMonth").GetDecimal());
        Assert.Equal(40m, insights.GetProperty("stillOpen").GetDecimal());
    }

    // Sentence 5: the two figures are different questions, and a settlement only
    // answers one of them. What was fronted this month is a fact about the month
    // and never changes; what is still out is a fact about right now.
    [Fact]
    public async Task Settling_closes_the_open_figure_without_rewriting_what_was_fronted()
    {
        var world = await SeedAsync();

        var expenseId = await CreateExpenseAsync(
            world, amount: 60m, expectedReimbursement: 40m, date: FirstOfThisMonth);

        await SettleAsync(world, expenseId, world.OtherAccount, 15m);

        var insights = await InsightsAsync(world);

        Assert.Equal(40m, insights.GetProperty("frontedThisMonth").GetDecimal());
        Assert.Equal(25m, insights.GetProperty("stillOpen").GetDecimal());
        // And the month itself has not moved a cent.
        Assert.Equal(20m, insights.GetProperty("currentMonthTotal").GetDecimal());
    }

    [Fact]
    public async Task Still_open_spans_history_while_fronted_is_this_month_only()
    {
        var world = await SeedAsync();

        // Last month's split is long since off the "fronted this month" books,
        // but the money is still out, and that is exactly what the user forgets.
        await CreateExpenseAsync(
            world, amount: 100m, expectedReimbursement: 70m, date: FirstOfThisMonth.AddMonths(-1));
        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: 40m, date: FirstOfThisMonth);

        var insights = await InsightsAsync(world);

        Assert.Equal(40m, insights.GetProperty("frontedThisMonth").GetDecimal());
        Assert.Equal(110m, insights.GetProperty("stillOpen").GetDecimal());

        // Both months are netted, each at its own date.
        Assert.Equal(20m, insights.GetProperty("currentMonthTotal").GetDecimal());
        Assert.Equal(30m, insights.GetProperty("previousMonthTotal").GetDecimal());
    }

    // ── Category totals ───────────────────────────────────────────────────

    [Fact]
    public async Task An_unsplit_expense_contributes_its_whole_amount_to_the_category_total()
    {
        var world = await SeedAsync();

        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: null, date: new DateOnly(2026, 3, 10));
        await CreateExpenseAsync(world, amount: 40m, expectedReimbursement: null, date: new DateOnly(2026, 5, 10));

        var summary = await CategorySummaryAsync(world, world.Category);

        Assert.Equal(100m, summary.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task A_split_expense_contributes_only_the_users_share_to_the_category_total()
    {
        var world = await SeedAsync();

        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: 40m, date: new DateOnly(2026, 3, 10));
        await CreateExpenseAsync(world, amount: 40m, expectedReimbursement: null, date: new DateOnly(2026, 5, 10));

        var summary = await CategorySummaryAsync(world, world.Category);

        Assert.Equal(60m, summary.GetProperty("totalAmount").GetDecimal());
    }

    // Sentence 3: a settlement is not income. It credits an account and closes a
    // receivable, and the income side of the app is not told about it at all.
    [Fact]
    public async Task Income_statistics_are_untouched_by_a_settlement()
    {
        var world = await SeedAsync();

        (await world.Client.PostAsJsonAsync("/api/Income", new
        {
            amount = 200m,
            date = "2026-03-02",
            accountId = world.Account,
            categoryId = world.IncomeCategory
        })).EnsureSuccessStatusCode();

        var expenseId = await CreateExpenseAsync(
            world, amount: 60m, expectedReimbursement: 40m, date: new DateOnly(2026, 3, 10));

        var before = await CategorySummaryAsync(world, world.IncomeCategory);
        Assert.Equal(200m, before.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(200m, await ReportTotalAsync(world, includeExpenses: false, includeIncomes: true));

        await SettleAsync(world, expenseId, world.Account, 40m);

        // The account went up by 40 — and that is the only place in the app the
        // 40 shows up. An income category that grew here would put money the
        // user never earned into every income chart they own.
        var after = await CategorySummaryAsync(world, world.IncomeCategory);
        Assert.Equal(200m, after.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(200m, await ReportTotalAsync(world, includeExpenses: false, includeIncomes: true));

        var incomes = await world.Client.GetFromJsonAsync<JsonElement>("/api/Income");
        Assert.Equal(200m, incomes.EnumerateArray().Sum(i => i.GetProperty("amount").GetDecimal()));
    }

    // ── Reports ───────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unsplit_expense_reports_its_whole_amount_as_a_negative_row()
    {
        var world = await SeedAsync();

        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: null, date: new DateOnly(2026, 3, 10));

        var report = await ReportAsync(world, includeExpenses: true, includeIncomes: false);

        Assert.Equal(-60m, report.GetProperty("rows").EnumerateArray().Single().GetProperty("amount").GetDecimal());
        Assert.Equal(-60m, report.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_split_expense_reports_only_the_users_share()
    {
        var world = await SeedAsync();

        var expenseId = await CreateExpenseAsync(
            world, amount: 60m, expectedReimbursement: 40m, date: new DateOnly(2026, 3, 10));

        var report = await ReportAsync(world, includeExpenses: true, includeIncomes: false);
        Assert.Equal(-20m, report.GetProperty("rows").EnumerateArray().Single().GetProperty("amount").GetDecimal());
        Assert.Equal(-20m, report.GetProperty("total").GetDecimal());

        // Settling does not rewrite history here either.
        await SettleAsync(world, expenseId, world.OtherAccount, 40m);

        var again = await ReportAsync(world, includeExpenses: true, includeIncomes: false);
        Assert.Equal(-20m, again.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task The_csv_export_carries_the_net_figure_the_preview_showed()
    {
        var world = await SeedAsync();

        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: 40m, date: new DateOnly(2026, 3, 10));

        var export = await world.Client.PostAsJsonAsync("/api/Reports/transactions/export", ReportBody(true, false));
        export.EnsureSuccessStatusCode();
        var csv = await export.Content.ReadAsStringAsync();

        // A spreadsheet that disagreed with the on-screen report would be the
        // worst of the two to discover.
        Assert.Contains("-20", csv);
        Assert.DoesNotContain("-60", csv);
    }

    #region Helpers

    private sealed record World(
        HttpClient Client, Guid Currency, Guid Account, Guid OtherAccount, Guid Category, Guid IncomeCategory);

    private async Task<World> SeedAsync()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        // EUR, so every converted figure below is an identity: a user who never
        // saved preferences reads back the "EUR" default, and same-code amounts
        // pass through untouched. A USD account would make these assertions
        // depend on whatever rate the exchange-rate suite last wrote.
        var currency = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var account = await ApiClient.CreateAccountAsync(client, currency, balance: 1000m);
        var other = await ApiClient.CreateAccountAsync(client, currency, balance: 500m);
        var category = await ApiClient.CreateCategoryAsync(client, categoryType: 0);
        var incomeCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 1);

        return new World(client, currency, account, other, category, incomeCategory);
    }

    private static async Task<Guid> CreateExpenseAsync(
        World world, decimal amount, decimal? expectedReimbursement, DateOnly date, Guid? category = null)
    {
        var response = await world.Client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Dinner",
            amount,
            date = date.ToString("yyyy-MM-dd"),
            accountId = world.Account,
            categoryId = category ?? world.Category,
            expectedReimbursement
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task SettleAsync(World world, Guid expenseId, Guid accountId, decimal amount)
    {
        var listed = await world.Client.GetFromJsonAsync<JsonElement>("/api/Reimbursement");
        var reimbursementId = listed.EnumerateArray()
            .Single(r => r.GetProperty("expenseId").GetGuid() == expenseId)
            .GetProperty("id").GetGuid();

        var response = await world.Client.PostAsJsonAsync(
            $"/api/Reimbursement/{reimbursementId}/settlements",
            new { accountId, amount, date = Today.ToString("yyyy-MM-dd") });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateBudgetAsync(World world, decimal amount, Guid? categoryId)
    {
        var response = await world.Client.PostAsJsonAsync("/api/Budget", new { amount, period = 0, categoryId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> BudgetStatusAsync(World world, Guid budgetId, string month)
    {
        var statuses = await world.Client.GetFromJsonAsync<JsonElement>($"/api/Budget/status?month={month}");
        return statuses.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == budgetId);
    }

    private static decimal SubtotalFor(JsonElement status, string currency) =>
        status.GetProperty("subtotals")
            .EnumerateArray()
            .Where(s => s.GetProperty("currency").GetString() == currency)
            .Select(s => s.GetProperty("total").GetDecimal())
            .DefaultIfEmpty(0m)
            .Single();

    private static Task<JsonElement> InsightsAsync(World world) =>
        world.Client.GetFromJsonAsync<JsonElement>("/api/Insights");

    private static JsonElement CategoryFor(JsonElement insights, Guid categoryId) =>
        insights.GetProperty("categories")
            .EnumerateArray()
            .Single(c => c.GetProperty("categoryId").GetGuid() == categoryId);

    private static async Task<JsonElement> CategorySummaryAsync(World world, Guid categoryId)
    {
        var summaries = await world.Client.GetFromJsonAsync<JsonElement>("/api/Category");
        return summaries.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == categoryId);
    }

    private static object ReportBody(bool includeExpenses, bool includeIncomes) => new
    {
        from = "2026-01-01",
        to = "2026-12-31",
        includeExpenses,
        includeIncomes
    };

    private static async Task<JsonElement> ReportAsync(World world, bool includeExpenses, bool includeIncomes)
    {
        var response = await world.Client.PostAsJsonAsync(
            "/api/Reports/transactions/preview", ReportBody(includeExpenses, includeIncomes));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<decimal> ReportTotalAsync(World world, bool includeExpenses, bool includeIncomes) =>
        (await ReportAsync(world, includeExpenses, includeIncomes)).GetProperty("total").GetDecimal();

    #endregion
}
