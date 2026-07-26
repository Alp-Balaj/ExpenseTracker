using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Locks in Fix-2: account balance must equal initial + Σincome − Σexpense at
// every point, including after edits (same account and cross-account moves)
// and deletes, not just after create.
[Collection(IntegrationTestCollection.Name)]
public sealed class TransactionBalanceInvariantTests
{
    private readonly CustomWebApplicationFactory _factory;

    public TransactionBalanceInvariantTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public static IEnumerable<object[]> Resources()
    {
        // (route segment, sign applied to the account balance)
        yield return new object[] { "Expense", -1 };
        yield return new object[] { "Income", +1 };
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Create_adjusts_balance_by_signed_amount(string resource, int sign)
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 100m);
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        await CreateTransactionAsync(client, resource, accountId, categoryId, amount: 30m);

        var balance = await ApiClient.GetAccountBalanceAsync(client, accountId);
        Assert.Equal(100m + sign * 30m, balance);
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Edit_amount_on_same_account_adjusts_by_delta_only(string resource, int sign)
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 100m);
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        var id = await CreateTransactionAsync(client, resource, accountId, categoryId, amount: 30m);

        var put = await client.PutAsJsonAsync($"/api/{resource}/{id}", TransactionBody(accountId, categoryId, amount: 50m));
        put.EnsureSuccessStatusCode();

        var balance = await ApiClient.GetAccountBalanceAsync(client, accountId);
        Assert.Equal(100m + sign * 50m, balance);
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Edit_moving_to_another_account_reverses_old_and_applies_new(string resource, int sign)
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var sourceAccountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 100m);
        var destAccountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 200m);
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        var id = await CreateTransactionAsync(client, resource, sourceAccountId, categoryId, amount: 30m);

        var put = await client.PutAsJsonAsync($"/api/{resource}/{id}", TransactionBody(destAccountId, categoryId, amount: 30m));
        put.EnsureSuccessStatusCode();

        Assert.Equal(100m, await ApiClient.GetAccountBalanceAsync(client, sourceAccountId));
        Assert.Equal(200m + sign * 30m, await ApiClient.GetAccountBalanceAsync(client, destAccountId));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Delete_reverses_the_original_amount(string resource, int sign)
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 100m);
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        var id = await CreateTransactionAsync(client, resource, accountId, categoryId, amount: 30m);
        Assert.Equal(100m + sign * 30m, await ApiClient.GetAccountBalanceAsync(client, accountId));

        var delete = await client.DeleteAsync($"/api/{resource}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Equal(100m, await ApiClient.GetAccountBalanceAsync(client, accountId));
    }

    private static async Task<Guid> CreateTransactionAsync(HttpClient client, string resource, Guid accountId, Guid categoryId, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"/api/{resource}", TransactionBody(accountId, categoryId, amount));
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return dto.GetProperty("id").GetGuid();
    }

    private static object TransactionBody(Guid accountId, Guid categoryId, decimal amount) => new
    {
        title = "Test transaction",
        amount,
        date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
        accountId,
        categoryId
    };
}
