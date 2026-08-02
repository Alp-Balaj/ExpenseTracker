using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// C·4: getting everything out, and deleting everything.
//
// The load-bearing test in this file is the blast-radius one. Deleting an
// account is the single most destructive thing this API can do, it runs against
// a database holding real financial history (the plan's rule 1), and the only
// thing standing between "delete my rows" and "delete some rows" is a set of
// cascading foreign keys nobody looks at. So it is asserted from both sides:
// the requester's rows are all gone, and a second account created identically
// still has every one of its own.
[Collection(IntegrationTestCollection.Name)]
public sealed class AccountPortabilityTests
{
    private const string Password = "P@ssw0rd-123!";

    private readonly CustomWebApplicationFactory _factory;

    public AccountPortabilityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Populated(ApiClient.AuthenticatedUser User, Guid AccountId, Guid CategoryId);

    /// <summary>An account with one row in most of the user-owned tables.</summary>
    private async Task<Populated> PopulateAsync()
    {
        var user = await ApiClient.CreateAuthenticatedUserAsync(_factory);
        var client = user.Client;

        var currencyId = await ApiClient.CreateCurrencyAsync(client, "USD");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 5_000m);
        var otherAccountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1_000m);
        var expenseCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 0, name: "Groceries");
        var incomeCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 1, name: "Salary");

        (await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Weekly shop",
            amount = 42.50m,
            date = "2026-08-01",
            accountId,
            categoryId = expenseCategory,
            description = "REWE"
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/Income", new
        {
            amount = 2_500m,
            date = "2026-08-02",
            accountId,
            categoryId = incomeCategory,
            description = "August salary"
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/Transfer", new
        {
            amount = 100m,
            date = "2026-08-03",
            fromAccountId = accountId,
            toAccountId = otherAccountId,
            note = "To savings"
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 400m,
            period = 0,
            categoryId = expenseCategory
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/PlannedTransaction", new
        {
            title = "Rent",
            amount = 950m,
            kind = 0,
            recurrence = 2,
            startDate = "2026-08-01",
            categoryId = expenseCategory,
            fromAccountId = accountId
        })).EnsureSuccessStatusCode();

        return new Populated(user, accountId, expenseCategory);
    }

    // ── Export ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_json_export_carries_every_table_the_account_owns()
    {
        var fx = await PopulateAsync();

        var response = await fx.User.Client.GetAsync("/api/Export/json");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("expensetracker-export-", response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName ?? string.Empty);

        var export = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, export.GetProperty("formatVersion").GetInt32());
        Assert.Equal(fx.User.Email, export.GetProperty("profile").GetProperty("email").GetString());

        // Nothing is a filtered view: every list the account has anything in must
        // be present, or "export everything" is a lie.
        Assert.NotEmpty(export.GetProperty("currencies").EnumerateArray());
        Assert.NotEmpty(export.GetProperty("categories").EnumerateArray());
        Assert.NotEmpty(export.GetProperty("accounts").EnumerateArray());
        Assert.NotEmpty(export.GetProperty("expenses").EnumerateArray());
        Assert.NotEmpty(export.GetProperty("incomes").EnumerateArray());
        Assert.NotEmpty(export.GetProperty("transfers").EnumerateArray());
        Assert.NotEmpty(export.GetProperty("budgets").EnumerateArray());
        Assert.NotEmpty(export.GetProperty("plannedTransactions").EnumerateArray());
    }

    [Fact]
    public async Task The_export_keeps_the_relationships_and_names_them()
    {
        var fx = await PopulateAsync();

        var export = await (await fx.User.Client.GetAsync("/api/Export/json"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var expense = export.GetProperty("expenses").EnumerateArray().Single();

        // Ids so it can be rebuilt elsewhere, names so the file is legible on its
        // own — an export that needs this app to interpret it is not an export.
        Assert.Equal(fx.AccountId, expense.GetProperty("accountId").GetGuid());
        Assert.Equal(fx.CategoryId, expense.GetProperty("categoryId").GetGuid());
        Assert.Equal("Groceries", expense.GetProperty("categoryName").GetString());
        Assert.Equal("USD", expense.GetProperty("currencyCode").GetString());
        Assert.Equal("Weekly shop", expense.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_export_contains_nobody_elses_rows()
    {
        var stranger = await PopulateAsync();
        var mine = await PopulateAsync();

        var export = await (await mine.User.Client.GetAsync("/api/Export/json"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var accountIds = export.GetProperty("accounts").EnumerateArray()
            .Select(a => a.GetProperty("id").GetGuid())
            .ToList();

        Assert.Contains(mine.AccountId, accountIds);
        Assert.DoesNotContain(stranger.AccountId, accountIds);
        Assert.Equal(mine.User.Email, export.GetProperty("profile").GetProperty("email").GetString());
    }

    [Fact]
    public async Task The_csv_export_is_one_sheet_of_transactions_with_a_utf8_marker()
    {
        var fx = await PopulateAsync();

        var response = await fx.User.Client.GetAsync("/api/Export/csv");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        // Without the BOM, Excel reads UTF-8 as the local codepage and a café
        // becomes a cafÃ©.
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

        var csv = System.Text.Encoding.UTF8.GetString(bytes.Skip(3).ToArray());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Date,Kind,Title,Category,Account,Amount,Currency,Description", lines[0]);
        Assert.Contains(lines, l => l.Contains("Expense") && l.Contains("-42.50"));
        Assert.Contains(lines, l => l.Contains("Income") && l.Contains("2500"));
        // Both halves of the transfer, so the sheet's own arithmetic balances.
        Assert.Contains(lines, l => l.StartsWith("2026-08-03,Transfer in"));
        Assert.Contains(lines, l => l.StartsWith("2026-08-03,Transfer out"));
    }

    [Fact]
    public async Task A_description_that_looks_like_a_formula_is_neutralised_in_the_csv()
    {
        var fx = await PopulateAsync();

        (await fx.User.Client.PostAsJsonAsync("/api/Expense", new
        {
            title = "=cmd|'/c calc'!A1",
            amount = 1m,
            date = "2026-08-05",
            accountId = fx.AccountId,
            categoryId = fx.CategoryId
        })).EnsureSuccessStatusCode();

        var bytes = await (await fx.User.Client.GetAsync("/api/Export/csv")).Content.ReadAsByteArrayAsync();
        var csv = System.Text.Encoding.UTF8.GetString(bytes.Skip(3).ToArray());

        // A CSV import (Sugg-6) is a live path from a bank's memo line into a
        // formula on someone's desktop; the leading apostrophe closes it.
        Assert.Contains("'=cmd", csv);
        Assert.DoesNotContain(",=cmd", csv);
    }

    [Fact]
    public async Task Export_requires_authentication()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/Export/json")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/Export/csv")).StatusCode);
    }

    // ── Deletion ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_an_account_removes_every_row_it_owns_and_nobody_elses()
    {
        var survivor = await PopulateAsync();
        var doomed = await PopulateAsync();

        var response = await doomed.User.Client.PostAsJsonAsync(
            "/api/User/delete-account", new { password = Password });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var deletedId = doomed.User.UserId;
        var survivorId = survivor.User.UserId;

        // Every user-owned table, from both directions. The cascade is a property
        // of the schema, so this is the only place it can be checked honestly.
        Assert.Empty(await db.Accounts.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.Currencies.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.Categories.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.Expenses.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.Incomes.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.Transfers.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.Budgets.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.PlannedTransactions.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.PlannedOccurrences.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.UserPreferences.Where(x => x.UserId == deletedId).ToListAsync());
        Assert.Empty(await db.RefreshTokens.Where(x => x.UserId == deletedId).ToListAsync());

        // ... and the blast radius stopped at the account boundary.
        Assert.NotEmpty(await db.Accounts.Where(x => x.UserId == survivorId).ToListAsync());
        Assert.NotEmpty(await db.Expenses.Where(x => x.UserId == survivorId).ToListAsync());
        Assert.NotEmpty(await db.Incomes.Where(x => x.UserId == survivorId).ToListAsync());
        Assert.NotEmpty(await db.Transfers.Where(x => x.UserId == survivorId).ToListAsync());
        Assert.NotEmpty(await db.Budgets.Where(x => x.UserId == survivorId).ToListAsync());
        Assert.NotEmpty(await db.PlannedTransactions.Where(x => x.UserId == survivorId).ToListAsync());
    }

    [Fact]
    public async Task The_surviving_account_can_still_be_used_afterwards()
    {
        var survivor = await PopulateAsync();
        var doomed = await PopulateAsync();

        (await doomed.User.Client.PostAsJsonAsync("/api/User/delete-account", new { password = Password }))
            .EnsureSuccessStatusCode();

        // Not just "rows exist" — the account still works, which is what a broken
        // cascade would take away.
        var expenses = await survivor.User.Client.GetAsync("/api/Expense");
        expenses.EnsureSuccessStatusCode();

        var created = await survivor.User.Client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Still here",
            amount = 5m,
            date = "2026-08-06",
            accountId = survivor.AccountId,
            categoryId = survivor.CategoryId
        });
        created.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_wrong_password_deletes_nothing()
    {
        var fx = await PopulateAsync();

        var response = await fx.User.Client.PostAsJsonAsync(
            "/api/User/delete-account", new { password = "not-the-password" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotEmpty(await db.Expenses.Where(x => x.UserId == fx.User.UserId).ToListAsync());
    }

    [Fact]
    public async Task Deletion_requires_authentication()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/User/delete-account", new { password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_deleted_account_cannot_log_in_again()
    {
        var fx = await PopulateAsync();

        (await fx.User.Client.PostAsJsonAsync("/api/User/delete-account", new { password = Password }))
            .EnsureSuccessStatusCode();

        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/User/login", new { email = fx.User.Email, password = Password });

        Assert.NotEqual(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Exporting_before_deleting_is_the_whole_point_and_both_work_in_one_session()
    {
        var fx = await PopulateAsync();

        var export = await (await fx.User.Client.GetAsync("/api/Export/json"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEmpty(export.GetProperty("expenses").EnumerateArray());

        (await fx.User.Client.PostAsJsonAsync("/api/User/delete-account", new { password = Password }))
            .EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Expenses.Where(x => x.UserId == fx.User.UserId).ToListAsync());
    }
}
