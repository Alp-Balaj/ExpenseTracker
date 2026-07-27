using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Covers Suggestion-3's budget status endpoint: spend must be scoped to the
// requested month and to the budget's category, the overall budget must span
// every category, and a budget must never accept another user's category or a
// second budget for the same one.
//
// Assertions read `subtotals` (raw per-currency spend) rather than `spent`,
// because `spent` is expressed in the user's base currency, which comes from
// the Mongo-backed preferences store — unavailable in this suite, so the
// converter degrades to subtotals-only by design (see CurrencyConversionService).
[Collection(IntegrationTestCollection.Name)]
public sealed class BudgetStatusTests
{
    private readonly CustomWebApplicationFactory _factory;

    public BudgetStatusTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task<Guid> CreateBudgetAsync(
        HttpClient client, decimal amount, Guid? categoryId)
    {
        var response = await client.PostAsJsonAsync("/api/Budget", new
        {
            amount,
            period = 0,
            categoryId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task CreateExpenseAsync(
        HttpClient client, Guid accountId, Guid categoryId, decimal amount, DateOnly date)
    {
        var response = await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Test expense",
            amount,
            date = date.ToString("yyyy-MM-dd"),
            accountId,
            categoryId
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> GetStatusAsync(HttpClient client, string month)
    {
        var response = await client.GetAsync($"/api/Budget/status?month={month}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static decimal SubtotalFor(JsonElement status, string currency) =>
        status.GetProperty("subtotals")
            .EnumerateArray()
            .Where(s => s.GetProperty("currency").GetString() == currency)
            .Select(s => s.GetProperty("total").GetDecimal())
            .DefaultIfEmpty(0m)
            .Single();

    private static JsonElement StatusFor(JsonElement statuses, Guid budgetId) =>
        statuses.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == budgetId);

    [Fact]
    public async Task Spend_is_scoped_to_the_requested_month_and_category()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "USD");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 10_000m);
        var groceriesId = await ApiClient.CreateCategoryAsync(client, name: "Groceries");
        var otherId = await ApiClient.CreateCategoryAsync(client, name: "Transport");

        var budgetId = await CreateBudgetAsync(client, amount: 300m, categoryId: groceriesId);

        // In the budgeted month and category: counts.
        await CreateExpenseAsync(client, accountId, groceriesId, 100m, new DateOnly(2026, 3, 1));
        await CreateExpenseAsync(client, accountId, groceriesId, 150m, new DateOnly(2026, 3, 31));
        // Right category, adjacent months: must not leak in either direction.
        await CreateExpenseAsync(client, accountId, groceriesId, 500m, new DateOnly(2026, 2, 28));
        await CreateExpenseAsync(client, accountId, groceriesId, 700m, new DateOnly(2026, 4, 1));
        // Right month, different category.
        await CreateExpenseAsync(client, accountId, otherId, 900m, new DateOnly(2026, 3, 15));

        var status = StatusFor(await GetStatusAsync(client, "2026-03"), budgetId);

        Assert.Equal(300m, status.GetProperty("limit").GetDecimal());
        Assert.Equal("2026-03", status.GetProperty("month").GetString());
        Assert.Equal("Groceries", status.GetProperty("categoryName").GetString());
        Assert.Equal(250m, SubtotalFor(status, "USD"));
    }

    [Fact]
    public async Task Overall_budget_spans_every_category()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "USD");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 10_000m);
        var firstId = await ApiClient.CreateCategoryAsync(client);
        var secondId = await ApiClient.CreateCategoryAsync(client);

        var overallId = await CreateBudgetAsync(client, amount: 1000m, categoryId: null);

        await CreateExpenseAsync(client, accountId, firstId, 120m, new DateOnly(2026, 5, 4));
        await CreateExpenseAsync(client, accountId, secondId, 80m, new DateOnly(2026, 5, 20));
        await CreateExpenseAsync(client, accountId, firstId, 400m, new DateOnly(2026, 6, 1));

        var status = StatusFor(await GetStatusAsync(client, "2026-05"), overallId);

        Assert.True(status.GetProperty("categoryId").ValueKind == JsonValueKind.Null);
        Assert.Equal("Overall", status.GetProperty("categoryName").GetString());
        Assert.Equal(200m, SubtotalFor(status, "USD"));
    }

    [Fact]
    public async Task A_month_with_no_spend_reports_the_limit_untouched()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "USD");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 10_000m);
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        var budgetId = await CreateBudgetAsync(client, amount: 300m, categoryId);
        await CreateExpenseAsync(client, accountId, categoryId, 250m, new DateOnly(2026, 8, 10));

        // Budgets recur, so the next month re-evaluates the same row from zero.
        var status = StatusFor(await GetStatusAsync(client, "2026-09"), budgetId);

        Assert.Equal(300m, status.GetProperty("limit").GetDecimal());
        Assert.Equal(0m, status.GetProperty("spent").GetDecimal());
        Assert.Equal(300m, status.GetProperty("remaining").GetDecimal());
        Assert.Empty(status.GetProperty("subtotals").EnumerateArray());
    }

    [Fact]
    public async Task Status_rejects_a_malformed_month()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        var response = await client.GetAsync("/api/Budget/status?month=2026-07-15");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Budget_against_another_users_category_is_rejected()
    {
        var owner = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var ownerCategoryId = await ApiClient.CreateCategoryAsync(owner);

        var intruder = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        var response = await intruder.PostAsJsonAsync("/api/Budget", new
        {
            amount = 100m,
            period = 0,
            categoryId = ownerCategoryId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_second_budget_for_the_same_category_is_rejected()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        await CreateBudgetAsync(client, amount: 200m, categoryId);

        var duplicate = await client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 300m,
            period = 0,
            categoryId
        });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task A_second_overall_budget_is_rejected()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        await CreateBudgetAsync(client, amount: 1000m, categoryId: null);

        var duplicate = await client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 1500m,
            period = 0,
            categoryId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Editing_a_budget_in_place_is_not_treated_as_a_duplicate()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        var budgetId = await CreateBudgetAsync(client, amount: 200m, categoryId);

        var update = await client.PutAsJsonAsync($"/api/Budget/{budgetId}", new
        {
            id = budgetId,
            amount = 450m,
            period = 0,
            categoryId
        });

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var status = StatusFor(await GetStatusAsync(client, "2026-03"), budgetId);
        Assert.Equal(450m, status.GetProperty("limit").GetDecimal());
    }

    [Fact]
    public async Task Budget_on_a_non_expense_category_is_rejected()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var incomeCategoryId = await ApiClient.CreateCategoryAsync(client, categoryType: 1);

        var response = await client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 100m,
            period = 0,
            categoryId = incomeCategoryId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_users_budgets_are_invisible()
    {
        var owner = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var categoryId = await ApiClient.CreateCategoryAsync(owner);
        var budgetId = await CreateBudgetAsync(owner, amount: 500m, categoryId);

        var intruder = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        var get = await intruder.GetAsync($"/api/Budget/{budgetId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var statuses = await GetStatusAsync(intruder, "2026-03");
        Assert.Empty(statuses.EnumerateArray());
    }
}
