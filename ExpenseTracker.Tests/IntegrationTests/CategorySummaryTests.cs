using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Suggestion-9 replaced the category summary's four-passes-over-every-row
// aggregation with one grouped SQL query per category type. These tests pin the
// numbers that rewrite has to keep producing: totals per type, per-currency
// subtotals when a category mixes currencies, and empty categories reporting
// zero rather than disappearing.
[Collection(IntegrationTestCollection.Name)]
public sealed class CategorySummaryTests
{
    private readonly CustomWebApplicationFactory _factory;

    public CategorySummaryTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task<JsonElement> GetSummariesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/Category");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement SummaryFor(JsonElement summaries, Guid categoryId) =>
        summaries.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == categoryId);

    private static decimal SubtotalFor(JsonElement summary, string currency) =>
        summary.GetProperty("subtotals")
            .EnumerateArray()
            .Where(s => s.GetProperty("currency").GetString() == currency)
            .Select(s => s.GetProperty("total").GetDecimal())
            .DefaultIfEmpty(0m)
            .Single();

    [Fact]
    public async Task Expense_totals_are_summed_per_category()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "USD");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 10_000m);
        var groceries = await ApiClient.CreateCategoryAsync(client, name: "Groceries");
        var transport = await ApiClient.CreateCategoryAsync(client, name: "Transport");

        await PostExpenseAsync(client, accountId, groceries, 40m);
        await PostExpenseAsync(client, accountId, groceries, 60m);
        await PostExpenseAsync(client, accountId, transport, 25m);

        var summaries = await GetSummariesAsync(client);

        Assert.Equal(100m, SummaryFor(summaries, groceries).GetProperty("totalAmount").GetDecimal());
        Assert.Equal(25m, SummaryFor(summaries, transport).GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task A_category_with_no_rows_reports_zero_rather_than_vanishing()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var unusedId = await ApiClient.CreateCategoryAsync(client, name: "Unused");

        var summary = SummaryFor(await GetSummariesAsync(client), unusedId);

        Assert.Equal(0m, summary.GetProperty("totalAmount").GetDecimal());
        Assert.Empty(summary.GetProperty("subtotals").EnumerateArray());
    }

    [Fact]
    public async Task A_category_spanning_two_currencies_is_broken_down_per_currency()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        // Base currency is the "EUR" default for a user who never saved
        // preferences, so USD converts at its stored rate and EUR is already base.
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var usdId = await ApiClient.CreateCurrencyAsync(client, "USD", rate: 0.5m);

        var eurAccount = await ApiClient.CreateAccountAsync(client, eurId, balance: 10_000m);
        var usdAccount = await ApiClient.CreateAccountAsync(client, usdId, balance: 10_000m);

        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Travel");

        await PostExpenseAsync(client, eurAccount, categoryId, 30m);
        await PostExpenseAsync(client, usdAccount, categoryId, 40m);

        var summary = SummaryFor(await GetSummariesAsync(client), categoryId);

        Assert.Equal(30m, SubtotalFor(summary, "EUR"));
        Assert.Equal(40m, SubtotalFor(summary, "USD"));
        Assert.Equal("EUR", summary.GetProperty("baseCurrency").GetString());
        Assert.Equal(50m, summary.GetProperty("convertedTotal").GetDecimal());
        Assert.False(summary.GetProperty("hasMissingRates").GetBoolean());
    }

    [Fact]
    public async Task Each_category_type_is_totalled_from_its_own_table()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "USD");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 10_000m);

        var expenseCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 0);
        var incomeCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 1);
        var goalCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 2);
        var futureCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 3);

        await PostExpenseAsync(client, accountId, expenseCategory, 11m);

        (await client.PostAsJsonAsync("/api/Income", new
        {
            amount = 22m,
            date = "2026-02-02",
            accountId,
            categoryId = incomeCategory
        })).EnsureSuccessStatusCode();

        // A goal's SavedAmount is server-owned: it starts at 0 and only moves
        // through contributions, so the summary's 33 has to be booked as one.
        var goalResponse = await client.PostAsJsonAsync("/api/SavingGoal", new
        {
            title = "Goal",
            targetAmount = 100m,
            savedAmount = 0m,
            deadline = "2026-12-31",
            accountId,
            categoryId = goalCategory
        });
        goalResponse.EnsureSuccessStatusCode();
        var goalId = (await goalResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await client.PostAsJsonAsync($"/api/SavingContribution/{goalId}", new
        {
            goalId,
            amount = 33m,
            date = "2026-02-10"
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/FutureExpense", new
        {
            title = "Bill",
            amount = 44m,
            predictedDate = "2026-03-03",
            categoryId = futureCategory
        })).EnsureSuccessStatusCode();

        var summaries = await GetSummariesAsync(client);

        Assert.Equal(11m, SummaryFor(summaries, expenseCategory).GetProperty("totalAmount").GetDecimal());
        Assert.Equal(22m, SummaryFor(summaries, incomeCategory).GetProperty("totalAmount").GetDecimal());
        Assert.Equal(33m, SummaryFor(summaries, goalCategory).GetProperty("totalAmount").GetDecimal());
        Assert.Equal(44m, SummaryFor(summaries, futureCategory).GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task Another_users_amounts_never_reach_a_summary()
    {
        var owner = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(owner, "USD");
        var accountId = await ApiClient.CreateAccountAsync(owner, currencyId, balance: 10_000m);
        var ownerCategory = await ApiClient.CreateCategoryAsync(owner);
        await PostExpenseAsync(owner, accountId, ownerCategory, 500m);

        var intruder = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        Assert.Empty((await GetSummariesAsync(intruder)).EnumerateArray());
    }

    private static async Task PostExpenseAsync(
        HttpClient client, Guid accountId, Guid categoryId, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Test expense",
            amount,
            date = "2026-01-15",
            accountId,
            categoryId
        });
        response.EnsureSuccessStatusCode();
    }
}
