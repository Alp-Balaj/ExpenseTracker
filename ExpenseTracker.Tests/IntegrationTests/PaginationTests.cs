using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Covers Suggestion-9's skip/take contract on the list endpoints.
//
// The contract that matters most here is the one that is easy to break by
// accident: paging is opt-in. A request with no skip/take must still return the
// user's whole list, because every current client — dashboard totals, charts,
// CSV export — aggregates the full history itself and would silently report
// wrong numbers against a truncated one. The window is only applied when asked
// for, and X-Total-Count always describes the unwindowed total so a client can
// tell how much more there is.
[Collection(IntegrationTestCollection.Name)]
public sealed class PaginationTests
{
    private readonly CustomWebApplicationFactory _factory;

    public PaginationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task CreateExpenseAsync(
        HttpClient client, Guid accountId, Guid categoryId, decimal amount, DateOnly date)
    {
        var response = await client.PostAsJsonAsync("/api/Expense", new
        {
            title = $"Expense {date:yyyy-MM-dd}",
            amount,
            date = date.ToString("yyyy-MM-dd"),
            accountId,
            categoryId
        });
        response.EnsureSuccessStatusCode();
    }

    private static int TotalCount(HttpResponseMessage response) =>
        int.Parse(response.Headers.GetValues("X-Total-Count").Single());

    private static async Task<List<DateOnly>> DatesAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()
            .Select(e => DateOnly.Parse(e.GetProperty("date").GetString()!))
            .ToList();

    // A fresh user owning `count` expenses on consecutive days, oldest first.
    private async Task<HttpClient> SeedExpensesAsync(int count)
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "USD");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 1_000_000m);
        var categoryId = await ApiClient.CreateCategoryAsync(client);

        for (var i = 0; i < count; i++)
            await CreateExpenseAsync(client, accountId, categoryId, 10m, new DateOnly(2026, 1, 1).AddDays(i));

        return client;
    }

    [Fact]
    public async Task A_request_without_paging_params_returns_the_whole_list()
    {
        var client = await SeedExpensesAsync(7);

        var response = await client.GetAsync("/api/Expense");
        response.EnsureSuccessStatusCode();

        var dates = await DatesAsync(response);

        Assert.Equal(7, dates.Count);
        Assert.Equal(7, TotalCount(response));
    }

    [Fact]
    public async Task Take_returns_one_window_and_the_header_keeps_the_full_total()
    {
        var client = await SeedExpensesAsync(7);

        var response = await client.GetAsync("/api/Expense?skip=0&take=3");
        response.EnsureSuccessStatusCode();

        var dates = await DatesAsync(response);

        Assert.Equal(3, dates.Count);
        Assert.Equal(7, TotalCount(response));

        // Newest first: the last three days seeded.
        Assert.Equal(new DateOnly(2026, 1, 7), dates[0]);
        Assert.Equal(new DateOnly(2026, 1, 6), dates[1]);
        Assert.Equal(new DateOnly(2026, 1, 5), dates[2]);
    }

    [Fact]
    public async Task Consecutive_pages_cover_the_list_exactly_once()
    {
        var client = await SeedExpensesAsync(7);

        var seen = new List<DateOnly>();

        for (var skip = 0; skip < 7; skip += 3)
        {
            var page = await client.GetAsync($"/api/Expense?skip={skip}&take=3");
            page.EnsureSuccessStatusCode();
            seen.AddRange(await DatesAsync(page));
        }

        // No row repeated across pages, none missed: the ordering the window is
        // taken from has to be total for this to hold.
        Assert.Equal(7, seen.Count);
        Assert.Equal(7, seen.Distinct().Count());
    }

    [Fact]
    public async Task Skipping_past_the_end_returns_an_empty_page_not_an_error()
    {
        var client = await SeedExpensesAsync(3);

        var response = await client.GetAsync("/api/Expense?skip=100&take=10");
        response.EnsureSuccessStatusCode();

        Assert.Empty(await DatesAsync(response));
        Assert.Equal(3, TotalCount(response));
    }

    [Theory]
    [InlineData("skip=-1")]
    [InlineData("take=0")]
    [InlineData("take=-5")]
    [InlineData("take=501")]
    public async Task Nonsense_paging_params_are_rejected(string query)
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        var response = await client.GetAsync($"/api/Expense?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Paging_never_reaches_across_users()
    {
        await SeedExpensesAsync(5);

        var intruder = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        var response = await intruder.GetAsync("/api/Expense?skip=0&take=50");
        response.EnsureSuccessStatusCode();

        Assert.Empty(await DatesAsync(response));
        Assert.Equal(0, TotalCount(response));
    }

    [Fact]
    public async Task Every_list_endpoint_reports_a_total_count()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        await ApiClient.CreateCurrencyAsync(client, "USD");

        foreach (var endpoint in new[]
                 {
                     "/api/Account", "/api/Budget", "/api/Category", "/api/Currency",
                     "/api/Expense", "/api/FutureExpense", "/api/Income",
                     "/api/SavingContribution", "/api/SavingGoal", "/api/Transfer"
                 })
        {
            var response = await client.GetAsync(endpoint);
            response.EnsureSuccessStatusCode();

            Assert.True(
                response.Headers.Contains("X-Total-Count"),
                $"{endpoint} did not report X-Total-Count");
        }
    }
}
