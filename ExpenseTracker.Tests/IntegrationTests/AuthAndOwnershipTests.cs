using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Locks in Fix-8 (cross-user references 404, not leak-y 200/403), Fix-13 (auth
// required everywhere) and Fix-16 (DataAnnotations validation on DTOs -> 400).
[Collection(IntegrationTestCollection.Name)]
public sealed class AuthAndOwnershipTests
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthAndOwnershipTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_request_to_a_protected_endpoint_is_rejected()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Expense");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Getting_another_users_expense_returns_not_found()
    {
        var owner = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(owner);
        var accountId = await ApiClient.CreateAccountAsync(owner, currencyId, balance: 100m);
        var categoryId = await ApiClient.CreateCategoryAsync(owner);

        var create = await owner.PostAsJsonAsync("/api/Expense", new
        {
            title = "Groceries",
            amount = 20m,
            date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            accountId,
            categoryId
        });
        create.EnsureSuccessStatusCode();
        var expenseId = (await create.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();

        var intruder = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        var get = await intruder.GetAsync($"/api/Expense/{expenseId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var delete = await intruder.DeleteAsync($"/api/Expense/{expenseId}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Creating_an_expense_against_another_users_account_is_rejected()
    {
        var owner = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var ownerCurrencyId = await ApiClient.CreateCurrencyAsync(owner);
        var ownerAccountId = await ApiClient.CreateAccountAsync(owner, ownerCurrencyId, balance: 100m);

        var intruder = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var intruderCategoryId = await ApiClient.CreateCategoryAsync(intruder);

        var response = await intruder.PostAsJsonAsync("/api/Expense", new
        {
            title = "Not my account",
            amount = 20m,
            date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            accountId = ownerAccountId,
            categoryId = intruderCategoryId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100m, await ApiClient.GetAccountBalanceAsync(owner, ownerAccountId));
    }

    [Fact]
    public async Task Non_positive_amount_fails_dataannotations_validation()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 100m);
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        var response = await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Zero amount",
            amount = 0m,
            date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            accountId,
            categoryId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
