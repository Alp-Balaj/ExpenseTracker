using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Locks in Suggestion-7: a transfer moves money between two of the user's own
// accounts without ever reaching the income/expense totals, and both halves
// move (or disappear) together.
[Collection(IntegrationTestCollection.Name)]
public sealed class TransferInvariantTests
{
    private readonly CustomWebApplicationFactory _factory;

    public TransferInvariantTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_debits_the_source_and_credits_the_destination()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);

        await CreateTransferAsync(client, checking, savings, amount: 200m);

        Assert.Equal(800m, await ApiClient.GetAccountBalanceAsync(client, checking));
        Assert.Equal(700m, await ApiClient.GetAccountBalanceAsync(client, savings));
    }

    // The whole point of the separate table: the acceptance sketch in the note
    // is "monthly expense/income totals unchanged".
    [Fact]
    public async Task Transfer_does_not_show_up_as_income_or_expense()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);

        await CreateTransferAsync(client, checking, savings, amount: 200m);

        var expenses = await client.GetFromJsonAsync<JsonElement>("/api/Expense");
        var incomes = await client.GetFromJsonAsync<JsonElement>("/api/Income");

        Assert.Empty(expenses.EnumerateArray());
        Assert.Empty(incomes.EnumerateArray());
    }

    [Fact]
    public async Task Delete_puts_both_balances_back()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);

        var id = await CreateTransferAsync(client, checking, savings, amount: 200m);

        var delete = await client.DeleteAsync($"/api/Transfer/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(client, checking));
        Assert.Equal(500m, await ApiClient.GetAccountBalanceAsync(client, savings));
    }

    [Fact]
    public async Task Edit_amount_adjusts_both_sides_by_the_delta_only()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);

        var id = await CreateTransferAsync(client, checking, savings, amount: 200m);

        var put = await client.PutAsJsonAsync($"/api/Transfer/{id}", TransferBody(checking, savings, amount: 350m));
        put.EnsureSuccessStatusCode();

        Assert.Equal(650m, await ApiClient.GetAccountBalanceAsync(client, checking));
        Assert.Equal(850m, await ApiClient.GetAccountBalanceAsync(client, savings));
    }

    [Fact]
    public async Task Edit_repointing_the_destination_reverses_the_old_account()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);
        var investment = await ApiClient.CreateAccountAsync(client, currencyId, balance: 50m);

        var id = await CreateTransferAsync(client, checking, savings, amount: 200m);

        var put = await client.PutAsJsonAsync($"/api/Transfer/{id}", TransferBody(checking, investment, amount: 200m));
        put.EnsureSuccessStatusCode();

        Assert.Equal(800m, await ApiClient.GetAccountBalanceAsync(client, checking));
        Assert.Equal(500m, await ApiClient.GetAccountBalanceAsync(client, savings));
        Assert.Equal(250m, await ApiClient.GetAccountBalanceAsync(client, investment));
    }

    [Fact]
    public async Task Transfer_between_accounts_of_different_currencies_is_rejected()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var euros = await ApiClient.CreateCurrencyAsync(client, "EUR");
        var dollars = await ApiClient.CreateCurrencyAsync(client, "USD");
        var euroAccount = await ApiClient.CreateAccountAsync(client, euros, balance: 1000m);
        var dollarAccount = await ApiClient.CreateAccountAsync(client, dollars, balance: 500m);

        var response = await client.PostAsJsonAsync("/api/Transfer", TransferBody(euroAccount, dollarAccount, amount: 200m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(client, euroAccount));
        Assert.Equal(500m, await ApiClient.GetAccountBalanceAsync(client, dollarAccount));
    }

    [Fact]
    public async Task Transfer_to_the_same_account_is_rejected()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);

        var response = await client.PostAsJsonAsync("/api/Transfer", TransferBody(checking, checking, amount: 200m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(client, checking));
    }

    // Fix-8's rule: another user's account id must be indistinguishable from a
    // nonexistent one, and must not move any money.
    [Fact]
    public async Task Transfer_into_another_users_account_is_rejected()
    {
        var owner = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var ownerCurrency = await ApiClient.CreateCurrencyAsync(owner);
        var ownerAccount = await ApiClient.CreateAccountAsync(owner, ownerCurrency, balance: 1000m);

        var intruder = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var intruderCurrency = await ApiClient.CreateCurrencyAsync(intruder);
        var intruderAccount = await ApiClient.CreateAccountAsync(intruder, intruderCurrency, balance: 10m);

        var response = await intruder.PostAsJsonAsync("/api/Transfer", TransferBody(intruderAccount, ownerAccount, amount: 5m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10m, await ApiClient.GetAccountBalanceAsync(intruder, intruderAccount));
        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(owner, ownerAccount));
    }

    [Fact]
    public async Task Transfer_crediting_a_goal_books_a_contribution()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);
        var goalId = await CreateGoalAsync(client, savings);

        await CreateTransferAsync(client, checking, savings, amount: 200m, savingGoalId: goalId);

        Assert.Equal(200m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));

        var contributions = await client.GetFromJsonAsync<JsonElement>("/api/SavingContribution");
        Assert.Single(contributions.EnumerateArray());
    }

    [Fact]
    public async Task Deleting_a_goal_crediting_transfer_reverses_the_goal_total()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);
        var goalId = await CreateGoalAsync(client, savings);

        var id = await CreateTransferAsync(client, checking, savings, amount: 200m, savingGoalId: goalId);

        var delete = await client.DeleteAsync($"/api/Transfer/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Equal(0m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));

        var contributions = await client.GetFromJsonAsync<JsonElement>("/api/SavingContribution");
        Assert.Empty(contributions.EnumerateArray());
    }

    [Fact]
    public async Task Editing_the_amount_of_a_goal_crediting_transfer_keeps_the_goal_in_step()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);
        var goalId = await CreateGoalAsync(client, savings);

        var id = await CreateTransferAsync(client, checking, savings, amount: 200m, savingGoalId: goalId);

        var put = await client.PutAsJsonAsync($"/api/Transfer/{id}", TransferBody(checking, savings, amount: 350m, savingGoalId: goalId));
        put.EnsureSuccessStatusCode();

        Assert.Equal(350m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));

        var contributions = await client.GetFromJsonAsync<JsonElement>("/api/SavingContribution");
        Assert.Single(contributions.EnumerateArray());
    }

    [Fact]
    public async Task Dropping_the_goal_from_a_transfer_removes_its_contribution()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);
        var goalId = await CreateGoalAsync(client, savings);

        var id = await CreateTransferAsync(client, checking, savings, amount: 200m, savingGoalId: goalId);

        var put = await client.PutAsJsonAsync($"/api/Transfer/{id}", TransferBody(checking, savings, amount: 200m));
        put.EnsureSuccessStatusCode();

        Assert.Equal(0m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));

        var contributions = await client.GetFromJsonAsync<JsonElement>("/api/SavingContribution");
        Assert.Empty(contributions.EnumerateArray());

        // The money still moved — only the savings annotation went away.
        Assert.Equal(800m, await ApiClient.GetAccountBalanceAsync(client, checking));
        Assert.Equal(700m, await ApiClient.GetAccountBalanceAsync(client, savings));
    }

    // The goal's SavedAmount is meant to track money in the account behind it,
    // so a transfer landing somewhere else can't be counted toward it.
    [Fact]
    public async Task Crediting_a_goal_backed_by_a_different_account_is_rejected()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var checking = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1000m);
        var savings = await ApiClient.CreateAccountAsync(client, currencyId, balance: 500m);
        var investment = await ApiClient.CreateAccountAsync(client, currencyId, balance: 50m);
        var goalId = await CreateGoalAsync(client, investment);

        var response = await client.PostAsJsonAsync("/api/Transfer", TransferBody(checking, savings, amount: 200m, savingGoalId: goalId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(client, checking));
        Assert.Equal(0m, await ApiClient.GetSavingGoalSavedAmountAsync(client, goalId));
    }

    private static async Task<Guid> CreateTransferAsync(
        HttpClient client, Guid fromAccountId, Guid toAccountId, decimal amount, Guid? savingGoalId = null)
    {
        var response = await client.PostAsJsonAsync("/api/Transfer", TransferBody(fromAccountId, toAccountId, amount, savingGoalId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static object TransferBody(Guid fromAccountId, Guid toAccountId, decimal amount, Guid? savingGoalId = null) => new
    {
        amount,
        date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
        note = "Test transfer",
        fromAccountId,
        toAccountId,
        savingGoalId
    };

    private static async Task<Guid> CreateGoalAsync(HttpClient client, Guid accountId)
    {
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        var response = await client.PostAsJsonAsync("/api/SavingGoal", new
        {
            title = "Emergency fund",
            targetAmount = 5000m,
            savedAmount = 0m,
            deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)).ToString("yyyy-MM-dd"),
            accountId,
            categoryId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
