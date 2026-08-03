using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Locks in Fix-5: SavingGoal.SavedAmount must equal Σcontributions at every
// point, including after edits (same goal and cross-goal moves) and deletes.
//
// Stream V added a second half to that invariant: every contribution also names
// the account holding its money, so Σcontributions-per-account is what an
// account has reserved. Both halves are asserted here.
[Collection(IntegrationTestCollection.Name)]
public sealed class SavingGoalInvariantTests
{
    private readonly CustomWebApplicationFactory _factory;

    public SavingGoalInvariantTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Add_contribution_increases_saved_amount()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var (goalId, accountId) = await CreateGoalAsync(client);

        await AddContributionAsync(client, goalId, accountId, amount: 40m);

        Assert.Equal(40m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
    }

    [Fact]
    public async Task Edit_contribution_amount_on_same_goal_adjusts_by_delta_only()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var (goalId, accountId) = await CreateGoalAsync(client);
        var contributionId = await AddContributionAsync(client, goalId, accountId, amount: 40m);

        var put = await client.PutAsJsonAsync($"/api/SavingContribution/{contributionId}", ContributionBody(goalId, accountId, amount: 90m));
        put.EnsureSuccessStatusCode();

        Assert.Equal(90m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
    }

    [Fact]
    public async Task Edit_moving_contribution_to_another_goal_reverses_old_and_applies_new()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var (sourceGoalId, accountId) = await CreateGoalAsync(client);
        var destGoalId = await CreateGoalOnAsync(client, accountId);
        var contributionId = await AddContributionAsync(client, sourceGoalId, accountId, amount: 40m);

        var put = await client.PutAsJsonAsync($"/api/SavingContribution/{contributionId}", ContributionBody(destGoalId, accountId, amount: 40m));
        put.EnsureSuccessStatusCode();

        Assert.Equal(0m, await ApiClient.GetSavingGoalSavedAmountAsync(client, sourceGoalId));
        Assert.Equal(40m, await ApiClient.GetSavingGoalSavedAmountAsync(client, destGoalId));
    }

    [Fact]
    public async Task Delete_contribution_reverses_the_original_amount()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var (goalId, accountId) = await CreateGoalAsync(client);
        var contributionId = await AddContributionAsync(client, goalId, accountId, amount: 40m);
        Assert.Equal(40m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));

        var delete = await client.DeleteAsync($"/api/SavingContribution/{contributionId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Equal(0m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
    }

    // ── Stream V: the money has an address ───────────────────────────────

    [Fact]
    public async Task Contributions_from_two_accounts_both_count_and_reserve_where_they_sit()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m, name: "Checking");
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m, name: "Savings");
        var goalId = await CreateGoalOnAsync(client, checking);

        await AddContributionAsync(client, goalId, checking, amount: 120m);
        await AddContributionAsync(client, goalId, savings, amount: 80m);

        // One goal, two addresses: the goal totals both, each account carries
        // only its own share.
        Assert.Equal(200m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
        Assert.Equal(120m, await ApiClient.GetAccountReservedAsync(client, checking));
        Assert.Equal(80m, await ApiClient.GetAccountReservedAsync(client, savings));
    }

    [Fact]
    public async Task A_contribution_without_an_account_is_rejected()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var (goalId, _) = await CreateGoalAsync(client);

        var response = await client.PostAsJsonAsync($"/api/SavingContribution/{goalId}", new
        {
            goalId,
            amount = 40m,
            date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_goal_holds_one_currency_and_rejects_an_account_in_another()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var euros = await ApiClient.CreateCurrencyAsync(client, code: "EUR");
        var dollars = await ApiClient.CreateCurrencyAsync(client, code: "USD");
        var euroAccount = await ApiClient.CreateAccountAsync(client, euros, balance: 500m, name: "Euro");
        var dollarAccount = await ApiClient.CreateAccountAsync(client, dollars, balance: 500m, name: "Dollar");
        var goalId = await CreateGoalOnAsync(client, euroAccount);

        // The first contribution fixes the goal's currency...
        await AddContributionAsync(client, goalId, euroAccount, amount: 100m);

        // ...and reserved has to stay subtractable from a balance, so a second
        // currency in the same envelope is refused rather than converted.
        var response = await client.PostAsJsonAsync($"/api/SavingContribution/{goalId}",
            ContributionBody(goalId, dollarAccount, amount: 50m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
        Assert.Equal(0m, await ApiClient.GetAccountReservedAsync(client, dollarAccount));
    }

    [Fact]
    public async Task An_empty_goal_may_be_funded_from_any_currency()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var euros = await ApiClient.CreateCurrencyAsync(client, code: "EUR");
        var dollars = await ApiClient.CreateCurrencyAsync(client, code: "USD");
        var euroAccount = await ApiClient.CreateAccountAsync(client, euros, balance: 500m, name: "Euro");
        var dollarAccount = await ApiClient.CreateAccountAsync(client, dollars, balance: 500m, name: "Dollar");

        // Opened against a euro account, but nothing has been put in it — the
        // deprecated backing account does not get a vote.
        var goalId = await CreateGoalOnAsync(client, euroAccount);

        await AddContributionAsync(client, goalId, dollarAccount, amount: 50m);

        Assert.Equal(50m, await ApiClient.GetAccountReservedAsync(client, dollarAccount));
    }

    [Fact]
    public async Task Releasing_money_is_a_negative_contribution()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var (goalId, accountId) = await CreateGoalAsync(client);
        await AddContributionAsync(client, goalId, accountId, amount: 120m);

        await AddContributionAsync(client, goalId, accountId, amount: -50m);

        Assert.Equal(70m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
        Assert.Equal(70m, await ApiClient.GetAccountReservedAsync(client, accountId));
    }

    [Fact]
    public async Task A_zero_contribution_is_rejected()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var (goalId, accountId) = await CreateGoalAsync(client);

        var response = await client.PostAsJsonAsync($"/api/SavingContribution/{goalId}",
            ContributionBody(goalId, accountId, amount: 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_goal_releases_everything_it_held()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var (goalId, accountId) = await CreateGoalAsync(client);
        await AddContributionAsync(client, goalId, accountId, amount: 200m);
        Assert.Equal(200m, await ApiClient.GetAccountReservedAsync(client, accountId));

        var delete = await client.DeleteAsync($"/api/SavingGoal/{goalId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // The contributions cascade with the goal, so the account's money is
        // its own again — and the balance itself never moved.
        Assert.Equal(0m, await ApiClient.GetAccountReservedAsync(client, accountId));
        Assert.Equal(500m, await ApiClient.GetAccountBalanceAsync(client, accountId));
    }

    [Fact]
    public async Task Spending_below_the_reserved_level_succeeds()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);
        var categoryId = await ApiClient.CreateCategoryAsync(client, categoryType: 2);
        var goalId = await CreateGoalOnAsync(client, accountId, categoryId);
        await AddContributionAsync(client, goalId, accountId, amount: 400m);

        var expenseCategoryId = await ApiClient.CreateCategoryAsync(client, categoryType: 0);
        var expense = await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Dipping into the holiday fund",
            amount = 300m,
            date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            accountId,
            categoryId = expenseCategoryId
        });

        // Warn, never block: the app records reality, it does not gatekeep it.
        expense.EnsureSuccessStatusCode();
        Assert.Equal(200m, await ApiClient.GetAccountBalanceAsync(client, accountId));
        Assert.Equal(400m, await ApiClient.GetAccountReservedAsync(client, accountId));
        // 200 usable against 400 reserved — the account is 200 short, which is
        // what the UI reads off these two numbers.
    }

    [Fact]
    public async Task A_goal_reports_its_contributions_and_currency()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, code: "GBP");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);
        var goalId = await CreateGoalOnAsync(client, accountId);
        await AddContributionAsync(client, goalId, accountId, amount: 25m);

        var response = await client.GetAsync($"/api/SavingGoal/{goalId}");
        response.EnsureSuccessStatusCode();
        var goal = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("GBP", goal.GetProperty("currencyCode").GetString());
        var contributions = goal.GetProperty("contributions").EnumerateArray().ToList();
        var single = Assert.Single(contributions);
        Assert.Equal(accountId, single.GetProperty("accountId").GetGuid());
    }

    private static async Task<(Guid GoalId, Guid AccountId)> CreateGoalAsync(HttpClient client)
    {
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);
        var goalId = await CreateGoalOnAsync(client, accountId);
        return (goalId, accountId);
    }

    private static async Task<Guid> CreateGoalOnAsync(HttpClient client, Guid accountId, Guid? categoryId = null)
    {
        var category = categoryId ?? await ApiClient.CreateCategoryAsync(client, categoryType: 2); // SavingGoals

        var response = await client.PostAsJsonAsync("/api/SavingGoal", new
        {
            title = $"Test Goal {Guid.NewGuid():N}",
            targetAmount = 1000m,
            savedAmount = 0m,
            deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)).ToString("yyyy-MM-dd"),
            accountId,
            categoryId = category
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddContributionAsync(HttpClient client, Guid goalId, Guid accountId, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"/api/SavingContribution/{goalId}", ContributionBody(goalId, accountId, amount));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object ContributionBody(Guid goalId, Guid accountId, decimal amount) => new
    {
        goalId,
        amount,
        date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
        accountId,
    };
}
