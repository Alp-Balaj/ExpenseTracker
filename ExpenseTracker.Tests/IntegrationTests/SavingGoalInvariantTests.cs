using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Locks in Fix-5: SavingGoal.SavedAmount must equal Σcontributions at every
// point, including after edits (same goal and cross-goal moves) and deletes.
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
        var goalId = await CreateGoalAsync(client);

        await AddContributionAsync(client, goalId, amount: 40m);

        Assert.Equal(40m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
    }

    [Fact]
    public async Task Edit_contribution_amount_on_same_goal_adjusts_by_delta_only()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var goalId = await CreateGoalAsync(client);
        var contributionId = await AddContributionAsync(client, goalId, amount: 40m);

        var put = await client.PutAsJsonAsync($"/api/SavingContribution/{contributionId}", ContributionBody(goalId, amount: 90m));
        put.EnsureSuccessStatusCode();

        Assert.Equal(90m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
    }

    [Fact]
    public async Task Edit_moving_contribution_to_another_goal_reverses_old_and_applies_new()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var sourceGoalId = await CreateGoalAsync(client);
        var destGoalId = await CreateGoalAsync(client);
        var contributionId = await AddContributionAsync(client, sourceGoalId, amount: 40m);

        var put = await client.PutAsJsonAsync($"/api/SavingContribution/{contributionId}", ContributionBody(destGoalId, amount: 40m));
        put.EnsureSuccessStatusCode();

        Assert.Equal(0m, await ApiClient.GetSavingGoalSavedAmountAsync(client, sourceGoalId));
        Assert.Equal(40m, await ApiClient.GetSavingGoalSavedAmountAsync(client, destGoalId));
    }

    [Fact]
    public async Task Delete_contribution_reverses_the_original_amount()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var goalId = await CreateGoalAsync(client);
        var contributionId = await AddContributionAsync(client, goalId, amount: 40m);
        Assert.Equal(40m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));

        var delete = await client.DeleteAsync($"/api/SavingContribution/{contributionId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Equal(0m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
    }

    private static async Task<Guid> CreateGoalAsync(HttpClient client)
    {
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId);
        var categoryId = await ApiClient.CreateCategoryAsync(client, categoryType: 2); // SavingGoals

        var response = await client.PostAsJsonAsync("/api/SavingGoal", new
        {
            title = $"Test Goal {Guid.NewGuid():N}",
            targetAmount = 1000m,
            savedAmount = 0m,
            deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)).ToString("yyyy-MM-dd"),
            accountId,
            categoryId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddContributionAsync(HttpClient client, Guid goalId, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"/api/SavingContribution/{goalId}", ContributionBody(goalId, amount));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object ContributionBody(Guid goalId, decimal amount) => new
    {
        goalId,
        amount,
        date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
    };
}
