using System.Net;
using System.Text;
using ExpenseTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExpenseTracker.Tests.UnitTests;

// The single most important thing Sugg-8 gets right or wrong: direction.
// frankfurter.app answers "?base=EUR" with "1 EUR = 1.1389 USD" (units of the quoted
// currency per base unit). This app stores the inverse -- amountInBase = amount *
// ExchangeRateToBase (Fix-10) -- so a stored USD rate must be EUR-per-dollar. Get this
// backwards and every converted total is wrong by the square of the rate, which looks
// plausible enough to ship.
public sealed class FrankfurterExchangeRateProviderTests
{
    private const string SampleResponse = """
        {"amount":1.0,"base":"EUR","date":"2026-07-27","rates":{"USD":1.1389,"GBP":0.85524,"JPY":186.37}}
        """;

    private static FrankfurterExchangeRateProvider CreateProvider(
        HttpStatusCode statusCode, string body)
    {
        var handler = new StubHandler(statusCode, body);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://upstream.test/") };
        return new FrankfurterExchangeRateProvider(
            new StubHttpClientFactory(client),
            NullLogger<FrankfurterExchangeRateProvider>.Instance);
    }

    [Fact]
    public async Task Inverts_upstream_quotes_into_the_amount_times_rate_direction()
    {
        var provider = CreateProvider(HttpStatusCode.OK, SampleResponse);

        var snapshot = await provider.GetLatestRatesAsync("EUR");

        Assert.NotNull(snapshot);
        // 1 / 1.1389 = 0.878040, i.e. one dollar is worth 0.87804 EUR -- which is
        // exactly what the live API returns for the mirrored query "?base=USD"
        // (EUR: 0.87804). The un-inverted 1.1389 would be the bug.
        Assert.Equal(0.878040m, snapshot!.RatesToBase["USD"]);
        Assert.Equal(1.169262m, snapshot.RatesToBase["GBP"]);
        Assert.Equal(0.005366m, snapshot.RatesToBase["JPY"]);
    }

    [Fact]
    public async Task Converting_an_amount_through_the_rate_lands_back_on_the_upstream_quote()
    {
        var provider = CreateProvider(HttpStatusCode.OK, SampleResponse);

        var snapshot = await provider.GetLatestRatesAsync("EUR");

        // The acceptance sketch in the note: 100 USD, converted, must read as the
        // 87.80 EUR the ECB rate implies -- not the 113.89 the inverse would give.
        var inBase = 100m * snapshot!.RatesToBase["USD"];
        Assert.Equal(87.80m, Math.Round(inBase, 2));
    }

    [Fact]
    public async Task Base_currency_is_always_exactly_one_even_though_upstream_omits_it()
    {
        var provider = CreateProvider(HttpStatusCode.OK, SampleResponse);

        var snapshot = await provider.GetLatestRatesAsync("EUR");

        Assert.Equal(1m, snapshot!.RatesToBase["EUR"]);
        Assert.Equal(new DateOnly(2026, 7, 27), snapshot.AsOf);
    }

    [Fact]
    public async Task Drops_non_positive_quotes_rather_than_inverting_them()
    {
        // A zero would invert to infinity and a negative to a negative rate; either
        // poisons every converted total downstream.
        var provider = CreateProvider(
            HttpStatusCode.OK,
            """{"amount":1.0,"base":"EUR","date":"2026-07-27","rates":{"USD":1.1389,"XXX":0,"YYY":-2}}""");

        var snapshot = await provider.GetLatestRatesAsync("EUR");

        Assert.True(snapshot!.RatesToBase.ContainsKey("USD"));
        Assert.False(snapshot.RatesToBase.ContainsKey("XXX"));
        Assert.False(snapshot.RatesToBase.ContainsKey("YYY"));
    }

    [Fact]
    public async Task Returns_null_when_the_upstream_does_not_cover_the_base_currency()
    {
        // How frankfurter answers for a currency outside the ECB set (ALL, RSD, ...).
        var provider = CreateProvider(HttpStatusCode.NotFound, """{"message":"not found"}""");

        Assert.Null(await provider.GetLatestRatesAsync("ALL"));
    }

    [Fact]
    public async Task Returns_null_when_the_upstream_is_unreachable()
    {
        var client = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://upstream.test/") };
        var provider = new FrankfurterExchangeRateProvider(
            new StubHttpClientFactory(client),
            NullLogger<FrankfurterExchangeRateProvider>.Instance);

        // Null, not an exception: a dead network must leave the caller's rates alone,
        // never take down the daily sweep.
        Assert.Null(await provider.GetLatestRatesAsync("EUR"));
    }

    [Fact]
    public async Task Returns_null_when_the_upstream_returns_an_empty_rate_table()
    {
        var provider = CreateProvider(
            HttpStatusCode.OK, """{"amount":1.0,"base":"EUR","date":"2026-07-27","rates":{}}""");

        Assert.Null(await provider.GetLatestRatesAsync("EUR"));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StubHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no route to host");
    }
}
