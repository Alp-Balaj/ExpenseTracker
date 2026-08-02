using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Stream C: signup seeds a starter catalogue — base currency + common currencies,
// expense/income categories, one "Main" account — inside the same transaction that
// creates the Identity user. These tests pin the exact catalogue, that it is seeded
// once per user only, and that one user's signup can never touch another's data.
[Collection(IntegrationTestCollection.Name)]
public sealed class OnboardingSeedTests
{
    private static readonly string[] ExpectedCurrencyCodes = { "EUR", "USD", "GBP" };

    private static readonly string[] ExpectedExpenseCategories =
        { "Groceries", "Eating Out", "Transport", "Housing", "Utilities", "Health", "Entertainment", "Shopping", "Other" };

    private static readonly string[] ExpectedIncomeCategories = { "Salary", "Other" };

    private readonly CustomWebApplicationFactory _factory;

    public OnboardingSeedTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task<List<JsonElement>> GetListAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    [Fact]
    public async Task Signup_seeds_exactly_the_starter_catalogue()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        // Currencies: EUR (the base, rate exactly 1) + USD + GBP, nothing else.
        var currencies = await GetListAsync(client, "/api/Currency");
        Assert.Equal(
            ExpectedCurrencyCodes.OrderBy(c => c),
            currencies.Select(c => c.GetProperty("code").GetString()).OrderBy(c => c));

        var eur = currencies.Single(c => c.GetProperty("code").GetString() == "EUR");
        Assert.Equal(1m, eur.GetProperty("exchangeRateToBase").GetDecimal());

        // Every seeded rate is a bootstrap value, so its age must read "unknown"
        // until the daily sweep stamps a real quote.
        Assert.All(currencies, c => Assert.Equal(JsonValueKind.Null, c.GetProperty("rateUpdatedAtUtc").ValueKind));

        // Categories: the expense set and the income set, each with a color.
        var categories = await GetListAsync(client, "/api/Category");
        Assert.Equal(
            ExpectedExpenseCategories.OrderBy(n => n),
            categories.Where(c => c.GetProperty("categoryType").GetInt32() == 0)
                .Select(c => c.GetProperty("name").GetString()).OrderBy(n => n));
        Assert.Equal(
            ExpectedIncomeCategories.OrderBy(n => n),
            categories.Where(c => c.GetProperty("categoryType").GetInt32() == 1)
                .Select(c => c.GetProperty("name").GetString()).OrderBy(n => n));
        Assert.All(categories, c => Assert.False(string.IsNullOrEmpty(c.GetProperty("color").GetString())));

        // One account: "Main", zero balance, denominated in the base currency.
        var accounts = await GetListAsync(client, "/api/Account");
        var account = Assert.Single(accounts);
        Assert.Equal("Main", account.GetProperty("name").GetString());
        Assert.Equal(0m, account.GetProperty("balance").GetDecimal());
        Assert.Equal("EUR", account.GetProperty("currencyCode").GetString());
        Assert.Equal(eur.GetProperty("id").GetGuid(), account.GetProperty("currencyId").GetGuid());

        // Preferences row records the base currency choice.
        var prefs = await client.GetFromJsonAsync<JsonElement>("/api/UserPreferences");
        Assert.Equal("EUR", prefs.GetProperty("baseCurrency").GetString());
    }

    [Fact]
    public async Task The_seeded_catalogue_is_immediately_usable_for_an_expense()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        var accountId = (await GetListAsync(client, "/api/Account")).Single().GetProperty("id").GetGuid();
        var groceriesId = (await GetListAsync(client, "/api/Category"))
            .Single(c => c.GetProperty("name").GetString() == "Groceries")
            .GetProperty("id").GetGuid();

        var create = await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "First expense",
            amount = 12.5m,
            date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            accountId,
            categoryId = groceriesId
        });
        create.EnsureSuccessStatusCode();

        Assert.Equal(-12.5m, await ApiClient.GetAccountBalanceAsync(client, accountId));
    }

    [Fact]
    public async Task A_second_signup_gets_its_own_catalogue_and_leaves_the_first_user_untouched()
    {
        var alice = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        // Alice makes the account her own before Bob shows up.
        var customCurrencyId = await ApiClient.CreateCurrencyAsync(alice, "SEK", rate: 0.09m);
        var customCategoryId = await ApiClient.CreateCategoryAsync(alice, name: "Alice's own");

        var currenciesBefore = (await GetListAsync(alice, "/api/Currency"))
            .Select(c => c.GetProperty("id").GetGuid()).OrderBy(id => id).ToList();
        var categoriesBefore = (await GetListAsync(alice, "/api/Category"))
            .Select(c => c.GetProperty("id").GetGuid()).OrderBy(id => id).ToList();
        var accountsBefore = (await GetListAsync(alice, "/api/Account"))
            .Select(a => a.GetProperty("id").GetGuid()).OrderBy(id => id).ToList();

        var bob = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        // Bob got exactly one fresh catalogue — Alice's rows are not his…
        var bobsCurrencies = (await GetListAsync(bob, "/api/Currency"))
            .Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(ExpectedCurrencyCodes.Length, bobsCurrencies.Count);
        Assert.Empty(bobsCurrencies.Intersect(currenciesBefore));
        Assert.DoesNotContain(customCurrencyId, bobsCurrencies);

        var bobsCategories = (await GetListAsync(bob, "/api/Category"))
            .Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(
            ExpectedExpenseCategories.Length + ExpectedIncomeCategories.Length,
            bobsCategories.Count);
        Assert.DoesNotContain(customCategoryId, bobsCategories);

        Assert.Single(await GetListAsync(bob, "/api/Account"));

        // …and his signup changed nothing of hers: same rows, before and after.
        Assert.Equal(currenciesBefore, (await GetListAsync(alice, "/api/Currency"))
            .Select(c => c.GetProperty("id").GetGuid()).OrderBy(id => id).ToList());
        Assert.Equal(categoriesBefore, (await GetListAsync(alice, "/api/Category"))
            .Select(c => c.GetProperty("id").GetGuid()).OrderBy(id => id).ToList());
        Assert.Equal(accountsBefore, (await GetListAsync(alice, "/api/Account"))
            .Select(a => a.GetProperty("id").GetGuid()).OrderBy(id => id).ToList());
    }
}
