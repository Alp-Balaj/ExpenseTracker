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
        HttpClient client,
        Guid accountId,
        Guid categoryId,
        decimal amount,
        DateOnly date,
        decimal? expectedReimbursement = null)
    {
        var response = await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Test expense",
            amount,
            date = date.ToString("yyyy-MM-dd"),
            accountId,
            categoryId,
            expectedReimbursement
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

    // ── Stream R: split bills ─────────────────────────────────────────────
    // The burn rate answers "how fast is my own money going", so the part other
    // people are expected to hand back is not burn — it is a scheduled inflow,
    // and it appears further down the same series.

    [Fact]
    public async Task A_user_who_never_splits_a_bill_gets_no_receivable_inflows_at_all()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await CreateAccountAsync(client, eurId, balance: 1_000m, amountType: 0);
        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Everyday");

        var d = Today.Day;
        await CreateExpenseAsync(
            client, accountId, categoryId, 3m * d, new DateOnly(Today.Year, Today.Month, 1));

        var projection = await GetProjectionAsync(client);

        // The regression pin for both of this file's sites: identical to what
        // the projection produced before reimbursements existed.
        Assert.Equal(1_000m - 3m * d, projection.GetProperty("startingBalance").GetDecimal());
        Assert.Equal(3m, projection.GetProperty("dailyBurnRate").GetDecimal());
        Assert.Equal(
            1_000m - 3m * d - 30m * 3m,
            projection.GetProperty("projectedEndBalance").GetDecimal());
        Assert.All(
            projection.GetProperty("points").EnumerateArray(),
            p => Assert.Equal(0m, p.GetProperty("plannedInflow").GetDecimal()));
    }

    [Fact]
    public async Task A_split_bill_burns_only_the_users_share()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await CreateAccountAsync(client, eurId, balance: 1_000m, amountType: 0);
        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Everyday");

        // 3 per elapsed day left the account, but a third of it was other
        // people's: the user is burning 2 a day, not 3.
        var d = Today.Day;
        await CreateExpenseAsync(
            client, accountId, categoryId, 3m * d, new DateOnly(Today.Year, Today.Month, 1),
            expectedReimbursement: 1m * d);

        var projection = await GetProjectionAsync(client);

        // Gross still left the account — that is what a balance is.
        Assert.Equal(1_000m - 3m * d, projection.GetProperty("startingBalance").GetDecimal());
        Assert.Equal(2m, projection.GetProperty("dailyBurnRate").GetDecimal());
    }

    [Fact]
    public async Task An_open_receivable_is_projected_as_an_inflow_a_fortnight_after_the_expense()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await CreateAccountAsync(client, eurId, balance: 1_000m, amountType: 0);
        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Everyday");

        // Dated today, so the fortnight lands well inside the 30-day horizon
        // whatever day of the month the suite runs on.
        await CreateExpenseAsync(client, accountId, categoryId, 100m, Today, expectedReimbursement: 60m);

        var projection = await GetProjectionAsync(client);

        Assert.Equal(60m, PointAt(projection, Today.AddDays(14)).GetProperty("plannedInflow").GetDecimal());
        Assert.Equal(0m, PointAt(projection, Today.AddDays(13)).GetProperty("plannedInflow").GetDecimal());
        Assert.Equal(0m, PointAt(projection, Today).GetProperty("plannedInflow").GetDecimal());
    }

    [Fact]
    public async Task A_receivable_already_older_than_a_fortnight_is_assumed_to_arrive_today()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await CreateAccountAsync(client, eurId, balance: 1_000m, amountType: 0);
        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Everyday");

        // Last month's dinner: the fortnight is long gone, but pretending the
        // money already arrived in the past would leave it out of the series
        // entirely. Same clamp an overdue pending bill gets.
        await CreateExpenseAsync(
            client, accountId, categoryId, 100m, Today.AddMonths(-1), expectedReimbursement: 60m);

        var projection = await GetProjectionAsync(client);

        Assert.Equal(60m, PointAt(projection, Today).GetProperty("plannedInflow").GetDecimal());
        // Last month's spending is not this month's burn.
        Assert.Equal(0m, projection.GetProperty("dailyBurnRate").GetDecimal());
        Assert.Equal(1_000m - 100m + 60m, PointAt(projection, Today).GetProperty("projectedBalance").GetDecimal());
    }

    [Fact]
    public async Task A_settled_receivable_stops_projecting_an_inflow()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await CreateAccountAsync(client, eurId, balance: 1_000m, amountType: 0);
        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Everyday");

        await CreateExpenseAsync(client, accountId, categoryId, 100m, Today, expectedReimbursement: 60m);

        var listed = await client.GetFromJsonAsync<JsonElement>("/api/Reimbursement");
        var reimbursementId = listed.EnumerateArray().Single().GetProperty("id").GetGuid();

        // Half of it comes back now; only the rest is still expected.
        (await client.PostAsJsonAsync($"/api/Reimbursement/{reimbursementId}/settlements", new
        {
            accountId,
            amount = 25m,
            date = Today.ToString("yyyy-MM-dd")
        })).EnsureSuccessStatusCode();

        var projection = await GetProjectionAsync(client);
        Assert.Equal(35m, PointAt(projection, Today.AddDays(14)).GetProperty("plannedInflow").GetDecimal());

        // And once it is closed out, the projection stops promising it.
        (await client.PostAsJsonAsync($"/api/Reimbursement/{reimbursementId}/settlements", new
        {
            accountId,
            amount = 35m,
            date = Today.ToString("yyyy-MM-dd")
        })).EnsureSuccessStatusCode();

        var settled = await GetProjectionAsync(client);
        Assert.All(
            settled.GetProperty("points").EnumerateArray(),
            p => Assert.Equal(0m, p.GetProperty("plannedInflow").GetDecimal()));
    }

    // The interaction the contract calls out by name. The committed-planned
    // subtraction is not an analysis total in its own right — it is the
    // correction term taken off the burn numerator so a recurring bill is not
    // counted once as history and again as a future due date. It therefore has
    // to be measured on exactly the same footing as that numerator. Left gross
    // against a net numerator, a planned bill that happens to be split subtracts
    // more than it ever contributed and drags the burn rate below the truth —
    // or into the Math.Max(0) floor, where the error becomes invisible.
    [Fact]
    public async Task A_committed_planned_bill_that_is_split_does_not_skew_the_burn_rate()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await CreateAccountAsync(client, eurId, balance: 1_000m, amountType: 0);
        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Everyday");

        // Everyday spend of 2 per elapsed day, no split.
        var d = Today.Day;
        await CreateExpenseAsync(
            client, accountId, categoryId, 2m * d, new DateOnly(Today.Year, Today.Month, 1));

        // A 100 flat share planned, due today, committed...
        await CreatePlannedAsync(
            client, "Rent", 100m, kind: 0, categoryId,
            startDate: Today, recurrence: 0, fromAccountId: accountId);
        var occurrences = await GetOccurrencesAsync(client);
        var occurrenceId = occurrences.EnumerateArray().Single().GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync(
            $"/api/PlannedTransaction/occurrences/{occurrenceId}/commit", new { })).EnsureSuccessStatusCode();

        // ...and then split after the fact, because a flatmate owes 60 of it.
        var rent = (await client.GetFromJsonAsync<JsonElement>("/api/Expense"))
            .EnumerateArray()
            .Single(e => e.GetProperty("title").GetString() == "Rent");

        (await client.PutAsJsonAsync($"/api/Expense/{rent.GetProperty("id").GetGuid()}", new
        {
            title = "Rent",
            amount = 100m,
            date = rent.GetProperty("date").GetString(),
            accountId,
            categoryId,
            expectedReimbursement = 60m
        })).EnsureSuccessStatusCode();

        var projection = await GetProjectionAsync(client);

        // The rent contributed 40 to the numerator and 40 came back out of it:
        // net zero, exactly as when it was unsplit and contributed 100 and 100.
        // The everyday burn is untouched at 2.
        Assert.Equal(2m, projection.GetProperty("dailyBurnRate").GetDecimal());
        Assert.Equal(1_000m - 2m * d - 100m, projection.GetProperty("startingBalance").GetDecimal());

        // The 60 is still owed, so it is still an expected inflow — once, and
        // for its open amount.
        var inflows = projection.GetProperty("points")
            .EnumerateArray()
            .Sum(p => p.GetProperty("plannedInflow").GetDecimal());
        Assert.Equal(60m, inflows);
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

    // ── Stream V: the line starts from usable money ──────────────────────

    // The regression pin the plan asks for: a user with no goals sees the
    // projection produced before envelopes existed, to the cent.
    [Fact]
    public async Task A_user_with_no_goals_projects_exactly_what_they_did_before_envelopes()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        await CreateAccountAsync(client, eurId, balance: 500m, amountType: 0);

        var projection = await GetProjectionAsync(client);

        Assert.Equal(500m, projection.GetProperty("startingBalance").GetDecimal());
        Assert.Equal(500m, projection.GetProperty("projectedEndBalance").GetDecimal());
    }

    [Fact]
    public async Task Reserved_money_is_not_projected_as_spendable()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await CreateAccountAsync(client, eurId, balance: 500m, amountType: 0);

        await ReserveAsync(client, accountId, amount: 200m);

        var projection = await GetProjectionAsync(client);

        // 500 in the account, 200 of it spoken for by a goal: the line may only
        // promise the 300 that is actually free.
        Assert.Equal(300m, projection.GetProperty("startingBalance").GetDecimal());
        Assert.Equal(300m, projection.GetProperty("projectedEndBalance").GetDecimal());
    }

    [Fact]
    public async Task Releasing_reserved_money_gives_it_back_to_the_projection()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var accountId = await CreateAccountAsync(client, eurId, balance: 500m, amountType: 0);

        var goalId = await ReserveAsync(client, accountId, amount: 200m);
        await ContributeAsync(client, goalId, accountId, amount: -80m);

        // 200 reserved less an 80 release leaves 120 held, so 380 of the 500 is
        // free again — releasing money is how it comes back.
        Assert.Equal(380m, (await GetProjectionAsync(client)).GetProperty("startingBalance").GetDecimal());
    }

    [Fact]
    public async Task Reserved_money_is_subtracted_in_its_own_currency_before_conversion()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var eurId = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var usdId = await ApiClient.CreateCurrencyAsync(client, "USD", rate: 2m);
        var euroAccount = await CreateAccountAsync(client, eurId, balance: 100m, amountType: 0);
        var dollarAccount = await CreateAccountAsync(client, usdId, balance: 100m, amountType: 0);

        await ReserveAsync(client, euroAccount, amount: 40m);
        await ReserveAsync(client, dollarAccount, amount: 40m);

        var projection = await GetProjectionAsync(client);

        // A goal only ever reserves in one currency, so the subtraction happens
        // account-side and the converter only ever sees usable figures:
        // (100−40) EUR + (100−40) USD at rate 2 = 60 + 120.
        //
        // The number discriminates: subtracting reserved after conversion, as
        // if it were already base currency, would give 300 − 80 = 220.
        Assert.Equal(180m, projection.GetProperty("startingBalance").GetDecimal());
    }

    /// <summary>Opens a goal on an account and reserves money against it.</summary>
    private static async Task<Guid> ReserveAsync(HttpClient client, Guid accountId, decimal amount)
    {
        var categoryId = await ApiClient.CreateCategoryAsync(client, categoryType: 2);

        var goal = await client.PostAsJsonAsync("/api/SavingGoal", new
        {
            title = $"Goal {Guid.NewGuid():N}",
            targetAmount = 1_000m,
            savedAmount = 0m,
            deadline = Today.AddYears(1).ToString("yyyy-MM-dd"),
            accountId,
            categoryId
        });
        goal.EnsureSuccessStatusCode();
        var goalId = (await goal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await ContributeAsync(client, goalId, accountId, amount);
        return goalId;
    }

    private static async Task ContributeAsync(HttpClient client, Guid goalId, Guid accountId, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"/api/SavingContribution/{goalId}", new
        {
            goalId,
            amount,
            date = Today.ToString("yyyy-MM-dd"),
            accountId
        });
        response.EnsureSuccessStatusCode();
    }
}
