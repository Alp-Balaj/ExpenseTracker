using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Covers Stream-B's cashflow projection: today's liquid balance walked forward
// a fixed 31 points (today + 30 days), losing the observed daily burn and
// applying every planned bill and income on its due date, all in the base
// currency ("EUR" for a user who never saved preferences).
//
// All dates are relative to "today" (UTC, same as the service) so the suite
// does not rot. Seeded schedules use offsets that stay inside (or outside)
// the fixed window whatever the calendar says: a monthly template is always
// at least 28 days from its previous due date, so only its first date can
// land inside the 30-day horizon.
[Collection(IntegrationTestCollection.Name)]
public sealed class CashflowProjectionTests
{
    private readonly CustomWebApplicationFactory _factory;

    public CashflowProjectionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client, Guid currencyId, decimal balance, int amountType)
    {
        var response = await client.PostAsJsonAsync("/api/Account", new
        {
            name = $"Account {Guid.NewGuid():N}",
            amountType,
            balance,
            currencyId
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

    private static async Task<Guid> CreatePlannedAsync(
        HttpClient client,
        string title,
        decimal amount,
        int kind,
        Guid categoryId,
        DateOnly startDate,
        int recurrence,
        Guid? fromAccountId = null,
        Guid? toAccountId = null)
    {
        var response = await client.PostAsJsonAsync("/api/PlannedTransaction", new
        {
            title,
            amount,
            kind,
            categoryId,
            fromAccountId,
            toAccountId,
            recurrence,
            startDate = startDate.ToString("yyyy-MM-dd"),
            isActive = true
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Materialises due occurrences (a side effect of listing) and returns them.</summary>
    private static async Task<JsonElement> GetOccurrencesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/PlannedTransaction/occurrences");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> GetProjectionAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/Cashflow/projection");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement PointAt(JsonElement projection, DateOnly date) =>
        projection.GetProperty("points")
            .EnumerateArray()
            .Single(p => p.GetProperty("date").GetString() == date.ToString("yyyy-MM-dd"));

    [Fact]
    public async Task Projection_combines_balances_planned_flows_and_burn_across_currencies()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var usdId = await ApiClient.CreateCurrencyAsync(client, "USD", rate: 0.5m);

        var checkingEur = await CreateAccountAsync(client, eurId, balance: 1_000m, amountType: 0);
        var savingsUsd = await CreateAccountAsync(client, usdId, balance: 200m, amountType: 1);
        // Credit is not spendable: must not count towards the opening balance.
        await CreateAccountAsync(client, eurId, balance: -500m, amountType: 3);

        var expenseCategory = await ApiClient.CreateCategoryAsync(client, name: "Everyday");
        var incomeCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 1, name: "Salary");

        // Everyday spend: 3 per elapsed day, posted on the 1st, so the burn
        // rate is exactly 3 whatever day of the month the suite runs on.
        var d = Today.Day;
        await CreateExpenseAsync(
            client, checkingEur, expenseCategory, 3m * d, new DateOnly(Today.Year, Today.Month, 1));

        // A one-off bill due today: materialised as a Pending occurrence and
        // charged to the USD account — 50 USD is 25 EUR on day one.
        await CreatePlannedAsync(
            client, "Insurance", 50m, kind: 0, expenseCategory,
            startDate: Today, recurrence: 0, fromAccountId: savingsUsd);

        // A weekly subscription starting in 3 days: never materialised (it is
        // in the future), so it must come from the template walk — four dues
        // land inside the window, at +3, +10, +17 and +24 days.
        await CreatePlannedAsync(
            client, "Weekly box", 10m, kind: 0, expenseCategory,
            startDate: Today.AddDays(3), recurrence: 1, fromAccountId: checkingEur);

        // A monthly income in 5 days; its second due date is at least 28 days
        // later, past the horizon.
        await CreatePlannedAsync(
            client, "Payday", 20m, kind: 1, incomeCategory,
            startDate: Today.AddDays(5), recurrence: 2, toAccountId: checkingEur);

        var occurrences = await GetOccurrencesAsync(client);
        Assert.Single(occurrences.EnumerateArray()); // only the bill due today

        var projection = await GetProjectionAsync(client);

        Assert.Equal("EUR", projection.GetProperty("baseCurrency").GetString());
        Assert.False(projection.GetProperty("hasMissingRates").GetBoolean());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), projection.GetProperty("today").GetString());

        // 1000 EUR checking - the 3d expense already posted + 200 USD savings
        // at 0.5. The credit card's -500 is excluded.
        var starting = 1_000m - 3m * d + 100m;
        Assert.Equal(starting, projection.GetProperty("startingBalance").GetDecimal());
        Assert.Equal(3m, projection.GetProperty("dailyBurnRate").GetDecimal());

        var points = projection.GetProperty("points");
        Assert.Equal(31, points.GetArrayLength());

        // Day 0 pays the pending bill but burns nothing — today's everyday
        // spending is already inside the account balances.
        var day0 = PointAt(projection, Today);
        Assert.Equal(25m, day0.GetProperty("plannedOutflow").GetDecimal());
        Assert.Equal(starting - 25m, day0.GetProperty("projectedBalance").GetDecimal());

        // +3 days: three days of burn and the first weekly box.
        var day3 = PointAt(projection, Today.AddDays(3));
        Assert.Equal(10m, day3.GetProperty("plannedOutflow").GetDecimal());
        Assert.Equal(starting - 25m - 3m * 3m - 10m, day3.GetProperty("projectedBalance").GetDecimal());

        // +5 days: payday lands on top of five days of burn.
        var day5 = PointAt(projection, Today.AddDays(5));
        Assert.Equal(20m, day5.GetProperty("plannedInflow").GetDecimal());
        Assert.Equal(
            starting - 25m - 5m * 3m - 10m + 20m,
            day5.GetProperty("projectedBalance").GetDecimal());

        // End of the window: 30 days of burn, the bill, four weekly boxes, one payday.
        var expectedEnd = starting - 25m - 30m * 3m - 4m * 10m + 20m;
        Assert.Equal(expectedEnd, projection.GetProperty("projectedEndBalance").GetDecimal());

        // The end-of-month figure is read off the same series.
        var endOfMonth = DateOnly.Parse(projection.GetProperty("endOfMonth").GetString()!);
        Assert.Equal(new DateOnly(Today.Year, Today.Month, 1).AddMonths(1).AddDays(-1), endOfMonth);
        Assert.Equal(
            PointAt(projection, endOfMonth).GetProperty("projectedBalance").GetDecimal(),
            projection.GetProperty("projectedEndOfMonthBalance").GetDecimal());
    }

    [Fact]
    public async Task Committed_planned_bills_do_not_inflate_the_burn_rate()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await CreateAccountAsync(client, eurId, balance: 1_000m, amountType: 0);
        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Everyday");

        // Everyday spend of 2 per elapsed day...
        var d = Today.Day;
        await CreateExpenseAsync(
            client, accountId, categoryId, 2m * d, new DateOnly(Today.Year, Today.Month, 1));

        // ...plus a 100 bill that was planned, fell due today and got committed.
        await CreatePlannedAsync(
            client, "Rent", 100m, kind: 0, categoryId,
            startDate: Today, recurrence: 0, fromAccountId: accountId);
        var occurrences = await GetOccurrencesAsync(client);
        var occurrenceId = occurrences.EnumerateArray().Single().GetProperty("id").GetGuid();
        var commit = await client.PostAsJsonAsync(
            $"/api/PlannedTransaction/occurrences/{occurrenceId}/commit", new { });
        commit.EnsureSuccessStatusCode();

        var projection = await GetProjectionAsync(client);

        // The committed rent left the balance (1000 - 2d - 100) but must not
        // read as everyday spending: the burn rate stays 2, not 2 + 100/d.
        var starting = 1_000m - 2m * d - 100m;
        Assert.Equal(starting, projection.GetProperty("startingBalance").GetDecimal());
        Assert.Equal(2m, projection.GetProperty("dailyBurnRate").GetDecimal());

        // The committed occurrence is spent history, not a pending outflow —
        // nothing may charge it a second time.
        var day0 = PointAt(projection, Today);
        Assert.Equal(0m, day0.GetProperty("plannedOutflow").GetDecimal());
        Assert.Equal(starting, day0.GetProperty("projectedBalance").GetDecimal());
        Assert.Equal(
            starting - 30m * 2m,
            projection.GetProperty("projectedEndBalance").GetDecimal());
    }

    [Fact]
    public async Task Empty_history_projects_a_flat_line_instead_of_failing()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        await CreateAccountAsync(client, eurId, balance: 500m, amountType: 0);

        var projection = await GetProjectionAsync(client);

        Assert.Equal(500m, projection.GetProperty("startingBalance").GetDecimal());
        Assert.Equal(0m, projection.GetProperty("dailyBurnRate").GetDecimal());
        Assert.Equal(31, projection.GetProperty("points").GetArrayLength());
        Assert.Equal(500m, projection.GetProperty("projectedEndBalance").GetDecimal());
        Assert.All(
            projection.GetProperty("points").EnumerateArray(),
            p => Assert.Equal(500m, p.GetProperty("projectedBalance").GetDecimal()));
    }
}
