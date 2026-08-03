using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Stream O's server half: the replay contract behind the offline outbox.
//
// The failure this exists to stop is specific and expensive. A queued expense
// is POSTed when the network returns; the request reaches the server, the
// expense is created, and the reply is lost on the way back — a tunnel, a
// captive portal, a phone changing cells. The client cannot tell that from "the
// server never got it", so it retries. Without a key on the server, that retry
// is a second expense and a second debit, and the account is quietly wrong.
//
// Every assertion here therefore checks the *balance* as well as the row count:
// the duplicate that matters is the one that moved money.
[Collection(IntegrationTestCollection.Name)]
public sealed class IdempotentWriteTests
{
    private const string KeyHeader = "Idempotency-Key";

    private readonly CustomWebApplicationFactory _factory;

    public IdempotentWriteTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Fixture(HttpClient Client, Guid AccountId, Guid CategoryId);

    private async Task<Fixture> ArrangeAsync(decimal balance = 1_000m)
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "EUR");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance);
        var categoryId = await ApiClient.CreateCategoryAsync(client, categoryType: 0);
        return new Fixture(client, accountId, categoryId);
    }

    private static HttpRequestMessage Post(Fixture fx, string? key, decimal amount = 25m, string title = "Coffee")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Expense")
        {
            Content = JsonContent.Create(new
            {
                title,
                amount,
                date = "2026-08-03",
                accountId = fx.AccountId,
                categoryId = fx.CategoryId
            })
        };

        if (key is not null) request.Headers.TryAddWithoutValidation(KeyHeader, key);
        return request;
    }

    private static async Task<int> CountExpensesAsync(HttpClient client, string title)
    {
        var response = await client.GetAsync("/api/Expense");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("items"))
            .EnumerateArray()
            .Count(e => e.GetProperty("title").GetString() == title);
    }

    // ── The contract ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_replayed_write_creates_one_expense_and_moves_the_balance_once()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        const string key = "outbox-1";

        var first = await fx.Client.SendAsync(Post(fx, key));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // The queue never saw the first reply, so it sends the same thing again.
        var second = await fx.Client.SendAsync(Post(fx, key));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        Assert.Equal(1, await CountExpensesAsync(fx.Client, "Coffee"));
        Assert.Equal(975m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task The_replay_is_indistinguishable_from_the_original()
    {
        var fx = await ArrangeAsync();
        const string key = "outbox-identical";

        var first = await fx.Client.SendAsync(Post(fx, key));
        var second = await fx.Client.SendAsync(Post(fx, key));

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        // Same status, same created id. This is what lets the client reconcile its
        // optimistic row against the real one instead of orphaning it — a bare 200
        // would leave the UI holding a row with no server id.
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(
            firstBody.GetProperty("id").GetGuid(),
            secondBody.GetProperty("id").GetGuid());

        Assert.True(second.Headers.Contains("Idempotency-Replayed"));
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task Ten_replays_are_still_one_expense()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        const string key = "outbox-persistent";

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await fx.Client.SendAsync(Post(fx, key));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        // A queue with a bad backoff, or a user who kept reopening the app.
        Assert.Equal(1, await CountExpensesAsync(fx.Client, "Coffee"));
        Assert.Equal(975m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task Concurrent_replays_of_one_key_still_create_one_expense()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        const string key = "outbox-race";

        // Two tabs coming back online at once, or a retry firing while the first
        // attempt is still in flight. Nothing in application code can arbitrate
        // this; the unique index does.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => fx.Client.SendAsync(Post(fx, key))));

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"unexpected {r.StatusCode}"));

        Assert.Equal(1, await CountExpensesAsync(fx.Client, "Coffee"));
        Assert.Equal(975m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    // ── Keys are scoped, and not magic ─────────────────────────────────────

    [Fact]
    public async Task Different_keys_are_different_expenses()
    {
        var fx = await ArrangeAsync(balance: 1_000m);

        await fx.Client.SendAsync(Post(fx, "outbox-a"));
        await fx.Client.SendAsync(Post(fx, "outbox-b"));

        // Two genuinely separate coffees, queued offline one after the other. The
        // key is minted when the expense is typed, so they differ.
        Assert.Equal(2, await CountExpensesAsync(fx.Client, "Coffee"));
        Assert.Equal(950m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task One_users_key_cannot_read_back_anothers_response()
    {
        var mine = await ArrangeAsync();
        var stranger = await ArrangeAsync();
        const string key = "collision";

        var theirs = await stranger.Client.SendAsync(Post(stranger, key, title: "Theirs"));
        theirs.EnsureSuccessStatusCode();

        var ours = await mine.Client.SendAsync(Post(mine, key, title: "Ours"));
        ours.EnsureSuccessStatusCode();

        var body = await ours.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ours", body.GetProperty("title").GetString());
        Assert.False(ours.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_endpoint_is_refused()
    {
        var fx = await ArrangeAsync();
        const string key = "outbox-crossed";

        (await fx.Client.SendAsync(Post(fx, key))).EnsureSuccessStatusCode();

        var income = new HttpRequestMessage(HttpMethod.Post, "/api/Income")
        {
            Content = JsonContent.Create(new
            {
                amount = 10m,
                date = "2026-08-03",
                accountId = fx.AccountId,
                categoryId = fx.CategoryId
            })
        };
        income.Headers.TryAddWithoutValidation(KeyHeader, key);

        // Answering with the stored expense would be worse than refusing.
        var response = await fx.Client.SendAsync(income);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_key_nothing_changes()
    {
        var fx = await ArrangeAsync(balance: 1_000m);

        // The ordinary online path. Two identical hand-typed coffees are two
        // coffees, and the filter must not quietly deduplicate them.
        (await fx.Client.SendAsync(Post(fx, key: null))).EnsureSuccessStatusCode();
        (await fx.Client.SendAsync(Post(fx, key: null))).EnsureSuccessStatusCode();

        Assert.Equal(2, await CountExpensesAsync(fx.Client, "Coffee"));
        Assert.Equal(950m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task An_absurdly_long_key_is_refused_rather_than_truncated()
    {
        var fx = await ArrangeAsync();

        var response = await fx.Client.SendAsync(Post(fx, new string('k', 200)));

        // Truncating would silently merge two different keys into one.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountExpensesAsync(fx.Client, "Coffee"));
    }

    // ── Failures must not pin the key ──────────────────────────────────────

    [Fact]
    public async Task A_rejected_write_leaves_the_key_free_for_a_corrected_retry()
    {
        var fx = await ArrangeAsync();
        const string key = "outbox-fixable";

        // Someone else's category: rejected, nothing applied.
        var stranger = await ArrangeAsync();
        var bad = new HttpRequestMessage(HttpMethod.Post, "/api/Expense")
        {
            Content = JsonContent.Create(new
            {
                title = "Coffee",
                amount = 25m,
                date = "2026-08-03",
                accountId = fx.AccountId,
                categoryId = stranger.CategoryId
            })
        };
        bad.Headers.TryAddWithoutValidation(KeyHeader, key);

        Assert.Equal(HttpStatusCode.BadRequest, (await fx.Client.SendAsync(bad)).StatusCode);

        // The outbox fixes the row and retries under the same queued id. Pinning
        // the key to the rejection would strand that entry for ever.
        var good = await fx.Client.SendAsync(Post(fx, key));
        Assert.Equal(HttpStatusCode.Created, good.StatusCode);
        Assert.Equal(1, await CountExpensesAsync(fx.Client, "Coffee"));
    }

    [Fact]
    public async Task A_replayed_write_survives_a_full_round_trip_through_the_expense_list()
    {
        var fx = await ArrangeAsync(balance: 500m);
        const string key = "outbox-visible";

        var created = await fx.Client.SendAsync(Post(fx, key, amount: 12.34m, title: "Bus fare"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await fx.Client.SendAsync(Post(fx, key, amount: 12.34m, title: "Bus fare"));

        // The row the replay pointed at is a real, fetchable expense — not a
        // recorded response with nothing behind it.
        var fetched = await fx.Client.GetAsync($"/api/Expense/{id}");
        fetched.EnsureSuccessStatusCode();

        Assert.Equal(487.66m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }
}
