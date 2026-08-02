using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Covers Stream-B's insights endpoint: the current month measured against the
// previous three, overall and per category, with every figure converted into
// the base currency before any percentage is taken.
//
// Dates are computed relative to "today" (UTC, same as the service) so the
// suite does not rot; every seeded expense lands on the 1st of its month,
// which is always inside the month window regardless of when the tests run.
// A user who has never saved preferences reads back the "EUR" default, so
// EUR amounts pass through unconverted and USD carries an explicit rate.
[Collection(IntegrationTestCollection.Name)]
public sealed class InsightsTests
{
    private readonly CustomWebApplicationFactory _factory;

    public InsightsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
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

    private static async Task<JsonElement> GetInsightsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/Insights");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement CategoryFor(JsonElement insights, Guid categoryId) =>
        insights.GetProperty("categories")
            .EnumerateArray()
            .Single(c => c.GetProperty("categoryId").GetGuid() == categoryId);

    [Fact]
    public async Task Insights_compare_the_month_with_its_history_across_currencies()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var usdId = await ApiClient.CreateCurrencyAsync(client, "USD", rate: 0.5m);
        var eurAccount = await ApiClient.CreateAccountAsync(client, eurId, balance: 100_000m);
        var usdAccount = await ApiClient.CreateAccountAsync(client, usdId, balance: 100_000m);
        var groceriesId = await ApiClient.CreateCategoryAsync(client, name: "Groceries");
        var transportId = await ApiClient.CreateCategoryAsync(client, name: "Transport");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var month0 = new DateOnly(today.Year, today.Month, 1);
        var month1 = month0.AddMonths(-1);
        var month2 = month0.AddMonths(-2);
        var month3 = month0.AddMonths(-3);

        // Trailing window: 200 EUR, then 100 EUR, then last month 100 EUR +
        // 100 USD (= 50 EUR) of groceries and 40 EUR of transport.
        await CreateExpenseAsync(client, eurAccount, groceriesId, 200m, month3);
        await CreateExpenseAsync(client, eurAccount, groceriesId, 100m, month2);
        await CreateExpenseAsync(client, eurAccount, groceriesId, 100m, month1);
        await CreateExpenseAsync(client, usdAccount, groceriesId, 100m, month1);
        await CreateExpenseAsync(client, eurAccount, transportId, 40m, month1);
        // Current month: 300 EUR of groceries, transport dropped to zero.
        await CreateExpenseAsync(client, eurAccount, groceriesId, 300m, month0);

        var insights = await GetInsightsAsync(client);

        Assert.Equal(month0.ToString("yyyy-MM"), insights.GetProperty("month").GetString());
        Assert.Equal("EUR", insights.GetProperty("baseCurrency").GetString());
        Assert.False(insights.GetProperty("hasMissingRates").GetBoolean());
        Assert.Equal(3, insights.GetProperty("monthsWithHistory").GetInt32());

        Assert.Equal(300m, insights.GetProperty("currentMonthTotal").GetDecimal());
        // 100 EUR + 100 USD * 0.5 + 40 EUR.
        Assert.Equal(190m, insights.GetProperty("previousMonthTotal").GetDecimal());
        // (200 + 100 + 150 + 40) / 3 = 163.33.
        Assert.Equal(163.33m, insights.GetProperty("trailingAverage").GetDecimal());
        // (300 - 163.33) / 163.33 * 100 = 83.68.
        Assert.Equal(83.68m, insights.GetProperty("overallVsAveragePct").GetDecimal());

        var groceries = CategoryFor(insights, groceriesId);
        Assert.Equal("Groceries", groceries.GetProperty("categoryName").GetString());
        Assert.Equal(300m, groceries.GetProperty("currentMonthTotal").GetDecimal());
        Assert.Equal(150m, groceries.GetProperty("previousMonthTotal").GetDecimal());
        Assert.Equal(150m, groceries.GetProperty("momChangeAmount").GetDecimal());
        Assert.Equal(100m, groceries.GetProperty("momChangePct").GetDecimal());
        // (200 + 100 + 150) / 3.
        Assert.Equal(150m, groceries.GetProperty("trailingAverage").GetDecimal());
        Assert.Equal(100m, groceries.GetProperty("vsAveragePct").GetDecimal());

        var transport = CategoryFor(insights, transportId);
        Assert.Equal(0m, transport.GetProperty("currentMonthTotal").GetDecimal());
        Assert.Equal(40m, transport.GetProperty("previousMonthTotal").GetDecimal());
        Assert.Equal(-40m, transport.GetProperty("momChangeAmount").GetDecimal());
        Assert.Equal(-100m, transport.GetProperty("momChangePct").GetDecimal());
        // 40 / 3 = 13.33, and dropping to zero is 100% below it.
        Assert.Equal(13.33m, transport.GetProperty("trailingAverage").GetDecimal());
        Assert.Equal(-100m, transport.GetProperty("vsAveragePct").GetDecimal());

        // Biggest mover first: groceries moved 150, transport only 40.
        var names = insights.GetProperty("categories")
            .EnumerateArray()
            .Select(c => c.GetProperty("categoryName").GetString())
            .ToList();
        Assert.Equal(["Groceries", "Transport"], names);
    }

    [Fact]
    public async Task Cold_start_reports_missing_history_instead_of_dividing_by_zero()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await ApiClient.CreateAccountAsync(client, eurId, balance: 1_000m);
        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Groceries");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await CreateExpenseAsync(
            client, accountId, categoryId, 120m, new DateOnly(today.Year, today.Month, 1));

        var insights = await GetInsightsAsync(client);

        Assert.Equal(0, insights.GetProperty("monthsWithHistory").GetInt32());
        Assert.Equal(120m, insights.GetProperty("currentMonthTotal").GetDecimal());
        Assert.Equal(0m, insights.GetProperty("previousMonthTotal").GetDecimal());
        Assert.Equal(JsonValueKind.Null, insights.GetProperty("trailingAverage").ValueKind);
        Assert.Equal(JsonValueKind.Null, insights.GetProperty("overallVsAveragePct").ValueKind);

        var groceries = CategoryFor(insights, categoryId);
        Assert.Equal(120m, groceries.GetProperty("currentMonthTotal").GetDecimal());
        Assert.Equal(JsonValueKind.Null, groceries.GetProperty("momChangePct").ValueKind);
        Assert.Equal(JsonValueKind.Null, groceries.GetProperty("trailingAverage").ValueKind);
        Assert.Equal(JsonValueKind.Null, groceries.GetProperty("vsAveragePct").ValueKind);
    }
}
