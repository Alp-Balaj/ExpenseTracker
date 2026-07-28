using System.Net.Http.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Suggestion-9's acceptance sketch is stated in round trips, not in response
// bodies, so it is asserted that way here.
//
// The bounds below are deliberately a little above what the code costs today:
// they are regression guards against a query re-entering a loop, not a
// challenge to shave one more round trip off. What they must catch is any cost
// that grows with the number of *rows* — every one of these requests is seeded
// with several categories, currencies and transactions, so a per-row or
// per-category query would blow straight through them.
[Collection(IntegrationTestCollection.Name)]
public sealed class QueryCountTests
{
    private readonly CustomWebApplicationFactory _factory;

    public QueryCountTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Seeds a user whose data spans every category type and two currencies —
    // the shape that used to make the category endpoint re-read whole tables.
    private async Task<HttpClient> SeedBusyUserAsync()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        var eur = await ApiClient.CreateCurrencyAsync(client, "EUR", rate: 1m);
        var usd = await ApiClient.CreateCurrencyAsync(client, "USD", rate: 0.5m);
        var eurAccount = await ApiClient.CreateAccountAsync(client, eur, balance: 100_000m);
        var usdAccount = await ApiClient.CreateAccountAsync(client, usd, balance: 100_000m);

        for (var i = 0; i < 4; i++)
        {
            var expenseCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 0);
            var incomeCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 1);

            foreach (var account in new[] { eurAccount, usdAccount })
            {
                (await client.PostAsJsonAsync("/api/Expense", new
                {
                    title = $"Expense {i}",
                    amount = 10m,
                    date = $"2026-01-{i + 1:D2}",
                    accountId = account,
                    categoryId = expenseCategory
                })).EnsureSuccessStatusCode();

                (await client.PostAsJsonAsync("/api/Income", new
                {
                    amount = 20m,
                    date = $"2026-01-{i + 1:D2}",
                    accountId = account,
                    categoryId = incomeCategory
                })).EnsureSuccessStatusCode();
            }
        }

        return client;
    }

    private async Task<(int Queries, HttpResponseMessage Response)> MeasureAsync(
        HttpClient client, string url)
    {
        _factory.QueryCounter.Reset();
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (_factory.QueryCounter.Count, response);
    }

    [Fact]
    public async Task The_category_summary_costs_a_fixed_number_of_queries()
    {
        var client = await SeedBusyUserAsync();

        var (queries, _) = await MeasureAsync(client, "/api/Category");

        // 7: the category list, one grouped aggregate per category type (4),
        // and the preferences row plus currency list behind the converter.
        // Before this change the controller looped the four category types and
        // each pass re-read the categories, accounts, currencies and one whole
        // entity table — about 24 round trips, three of which scanned a full
        // table every time.
        Assert.True(queries <= 7, $"category summary took {queries} queries");
    }

    [Fact]
    public async Task Budget_status_costs_a_fixed_number_of_queries()
    {
        var client = await SeedBusyUserAsync();

        (await client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 1000m,
            period = 0,
            categoryId = (Guid?)null
        })).EnsureSuccessStatusCode();

        var (queries, _) = await MeasureAsync(client, "/api/Budget/status?month=2026-01");

        // 5: budgets, the month's spend grouped in SQL, categories, and the
        // converter's two. The month filter used to run in memory over every
        // expense the user had ever recorded.
        Assert.True(queries <= 5, $"budget status took {queries} queries");
    }

    [Fact]
    public async Task An_unpaged_list_costs_one_query_and_a_paged_one_costs_two()
    {
        var client = await SeedBusyUserAsync();

        var (unpaged, _) = await MeasureAsync(client, "/api/Expense");
        var (paged, _) = await MeasureAsync(client, "/api/Expense?skip=0&take=3");

        // Unpaged needs no COUNT: the list it returns is the total.
        Assert.Equal(1, unpaged);

        // Paged pays for one COUNT on top of the window.
        Assert.Equal(2, paged);
    }
}
