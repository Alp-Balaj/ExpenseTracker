using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Sugg-8: rates must refresh themselves, and every guard around that has to hold.
// Driven through POST /api/Currency/RefreshRates rather than the background timer,
// because the endpoint and the sweep share the same ExchangeRateUpdater -- this
// exercises the real DbContext writes without waiting 24 hours.
//
// The upstream is StubExchangeRateProvider (registered by the factory), so nothing
// here touches the network.
[Collection(IntegrationTestCollection.Name)]
public sealed class ExchangeRateRefreshTests
{
    private readonly CustomWebApplicationFactory _factory;

    public ExchangeRateRefreshTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task SetBaseCurrencyAsync(HttpClient client, string code)
    {
        var response = await client.PutAsJsonAsync("/api/UserPreferences", new
        {
            theme = "system",
            baseCurrency = code
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> RefreshAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/Currency/RefreshRates", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement CurrencyByCode(JsonElement payload, string code) =>
        payload.GetProperty("currencies")
            .EnumerateArray()
            .Single(c => c.GetProperty("code").GetString() == code);

    [Fact]
    public async Task Refresh_stores_the_rate_in_the_amount_times_rate_direction()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        await SetBaseCurrencyAsync(client, "EUR");
        // Deliberately nonsense to prove the value really is overwritten.
        await ApiClient.CreateCurrencyAsync(client, "USD", rate: 5m);

        var payload = await RefreshAsync(client);

        var usd = CurrencyByCode(payload, "USD");
        Assert.Equal(0.878040m, usd.GetProperty("exchangeRateToBase").GetDecimal());
        Assert.Equal("2026-07-27", payload.GetProperty("ratesAsOf").GetString());
        Assert.False(payload.GetProperty("providerUnavailable").GetBoolean());
    }

    [Fact]
    public async Task Refresh_stamps_the_rate_age_so_the_ui_can_show_it()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        await SetBaseCurrencyAsync(client, "EUR");
        await ApiClient.CreateCurrencyAsync(client, "GBP", rate: 1m);

        var before = DateTime.UtcNow.AddSeconds(-5);
        var payload = await RefreshAsync(client);

        var stamped = CurrencyByCode(payload, "GBP").GetProperty("rateUpdatedAtUtc").GetDateTime();
        Assert.InRange(stamped, before, DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Refresh_forces_the_base_currency_row_to_exactly_one()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        await SetBaseCurrencyAsync(client, "EUR");
        // A base currency at anything but 1 silently scales every total in the app.
        await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 0.5m);

        var payload = await RefreshAsync(client);

        Assert.Equal(1m, CurrencyByCode(payload, "EUR").GetProperty("exchangeRateToBase").GetDecimal());
    }

    [Fact]
    public async Task Refresh_leaves_currencies_the_upstream_does_not_publish_untouched()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        await SetBaseCurrencyAsync(client, "EUR");
        // The ECB set is ~30 currencies; ALL (Albanian lek) is not one of them, so the
        // hand-entered rate is the only rate there will ever be for it.
        await ApiClient.CreateCurrencyAsync(client, "ALL", rate: 0.010200m);

        var payload = await RefreshAsync(client);

        Assert.Equal(0.010200m, CurrencyByCode(payload, "ALL").GetProperty("exchangeRateToBase").GetDecimal());
        Assert.Contains(
            "ALL",
            payload.GetProperty("unsupportedCodes").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task Refresh_keeps_existing_rates_when_the_upstream_is_unavailable()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        // The stub publishes nothing against ALL, mirroring frankfurter's 404 for a
        // base currency it does not cover.
        await SetBaseCurrencyAsync(client, "ALL");
        await ApiClient.CreateCurrencyAsync(client, "USD", rate: 98.5m);

        var payload = await RefreshAsync(client);

        Assert.True(payload.GetProperty("providerUnavailable").GetBoolean());
        // The whole point of the fallback: a dead upstream must never blank or zero a
        // rate, only leave yesterday's in place.
        Assert.Equal(98.5m, CurrencyByCode(payload, "USD").GetProperty("exchangeRateToBase").GetDecimal());
    }

    [Fact]
    public async Task Refresh_never_writes_a_non_positive_rate()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        await SetBaseCurrencyAsync(client, "SEK");
        await ApiClient.CreateCurrencyAsync(client, "USD", rate: 9.4m);

        // An upstream that starts publishing zeros must not be believed.
        _factory.RateProvider.Snapshots["SEK"] = new ExchangeRateSnapshot(
            "SEK",
            new DateOnly(2026, 7, 27),
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 0m });

        try
        {
            var payload = await RefreshAsync(client);
            Assert.Equal(9.4m, CurrencyByCode(payload, "USD").GetProperty("exchangeRateToBase").GetDecimal());
        }
        finally
        {
            _factory.RateProvider.Snapshots.Remove("SEK");
        }
    }

    [Fact]
    public async Task Refresh_only_touches_the_calling_users_currencies()
    {
        var alice = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var bob = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        await SetBaseCurrencyAsync(alice, "EUR");
        await SetBaseCurrencyAsync(bob, "EUR");
        await ApiClient.CreateCurrencyAsync(alice, "USD", rate: 5m);
        await ApiClient.CreateCurrencyAsync(bob, "USD", rate: 7m);

        await RefreshAsync(alice);

        // The updater bypasses the user-scoped repository, so its own UserId filter is
        // the only thing standing between one user's refresh and another's data.
        var bobsCurrencies = await bob.GetFromJsonAsync<JsonElement>("/api/Currency");
        var bobsUsd = bobsCurrencies.EnumerateArray().Single(c => c.GetProperty("code").GetString() == "USD");
        Assert.Equal(7m, bobsUsd.GetProperty("exchangeRateToBase").GetDecimal());
    }

    [Fact]
    public async Task Editing_a_currency_without_changing_its_rate_does_not_refresh_the_rate_age()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        await SetBaseCurrencyAsync(client, "EUR");
        var id = await ApiClient.CreateCurrencyAsync(client, "GBP", rate: 1.169262m);

        var created = await client.GetFromJsonAsync<JsonElement>($"/api/Currency/{id}");
        var stampedAtCreation = created.GetProperty("rateUpdatedAtUtc").GetDateTime();

        // Renaming must not make a stale rate look freshly fetched.
        var rename = await client.PutAsJsonAsync($"/api/Currency/{id}", new
        {
            id,
            name = "Pound Sterling",
            code = "GBP",
            symbol = "£",
            exchangeRateToBase = 1.169262m
        });
        rename.EnsureSuccessStatusCode();

        var renamed = await client.GetFromJsonAsync<JsonElement>($"/api/Currency/{id}");
        Assert.Equal(stampedAtCreation, renamed.GetProperty("rateUpdatedAtUtc").GetDateTime());
        Assert.Equal("Pound Sterling", renamed.GetProperty("name").GetString());
    }
}
