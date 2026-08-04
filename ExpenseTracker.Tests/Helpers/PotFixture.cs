using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.Helpers;

/// <summary>
/// The multi-user fixture. This is a deliverable of Cycle U W1 in its own
/// right, not scaffolding that happened to be needed.
///
/// <para>
/// Until now every integration test in this suite authenticated exactly one
/// user. That was not a gap anybody had to work around, because until now every
/// row in the database belonged to exactly one person — but it does mean there
/// was no fixture that could even <em>express</em> a cross-user assertion, let
/// alone fail one. "Members cannot see each other's private data" was an
/// untested claim not because anyone skipped the test, but because the shape to
/// write it in did not exist.
/// </para>
///
/// <para>
/// What this gives every later wave: two or more signed-in clients against the
/// one Testcontainers Postgres, each with its own bearer token and its own
/// distinguishable display name, plus the pot verbs expressed once so a test
/// reads as a story about people rather than as a pile of URLs. W2 leans on it
/// for the Σ Bᵢ invariant across four members; W3 leans on it for the two-phase
/// settlement, where the whole point is that two different people act.
/// </para>
///
/// <para>
/// Anti-pattern this exists to prevent: reaching into <c>ApplicationDbContext</c>
/// to fabricate a second user's rows. Doing that would test the database and
/// skip the authorization layer — which, for a feature whose central claim is
/// "a non-member gets 404", is precisely the layer under test.
/// </para>
/// </summary>
public static class PotFixture
{
    /// <summary>
    /// Names chosen so a failure message says who, and so the four match W0's
    /// simulated cohort — the same people the browser pass logs in as.
    /// </summary>
    private static readonly (string First, string Last)[] Cohort =
    {
        ("Alp", "Balaj"),
        ("Mara", "Ionescu"),
        ("Tomas", "Novak"),
        ("Iris", "Vogel"),
    };

    /// <summary>Two signed-in strangers. The default arrangement for W1's tests.</summary>
    public static async Task<(ApiClient.AuthenticatedUser A, ApiClient.AuthenticatedUser B)> TwoUsersAsync(
        CustomWebApplicationFactory factory)
    {
        var users = await UsersAsync(factory, 2);
        return (users[0], users[1]);
    }

    /// <summary>
    /// <paramref name="count"/> signed-in users, in cohort order — which is also
    /// join order, which is W2's rounding tie-break. Created sequentially on
    /// purpose: signup writes a starter catalogue in a transaction, and the
    /// order they land in is the order the tie-break will read.
    /// </summary>
    public static async Task<IReadOnlyList<ApiClient.AuthenticatedUser>> UsersAsync(
        CustomWebApplicationFactory factory, int count)
    {
        Assert.InRange(count, 1, Cohort.Length);

        var users = new List<ApiClient.AuthenticatedUser>(count);
        for (var i = 0; i < count; i++)
            users.Add(await ApiClient.CreateAuthenticatedUserAsync(factory, Cohort[i].First, Cohort[i].Last));

        return users;
    }

    // ── The pot verbs ──────────────────────────────────────────────────────────

    public static async Task<JsonElement> CreatePotAsync(
        HttpClient client, string name = "Flat", string? currencyCode = null, decimal? lowBalanceThreshold = null)
    {
        var response = await client.PostAsJsonAsync("/api/Pot", new
        {
            name,
            currencyCode,
            lowBalanceThreshold
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    public static async Task<Guid> CreatePotIdAsync(
        HttpClient client, string name = "Flat", string? currencyCode = null, decimal? lowBalanceThreshold = null) =>
        (await CreatePotAsync(client, name, currencyCode, lowBalanceThreshold)).GetProperty("id").GetGuid();

    /// <summary>Issues an invite and hands back the code that goes in the link.</summary>
    public static async Task<string> InviteCodeAsync(HttpClient client, Guid potId)
    {
        var response = await client.PostAsync($"/api/Pot/{potId}/invites", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("code").GetString()!;
    }

    public static async Task<JsonElement> InviteAsync(HttpClient client, Guid potId)
    {
        var response = await client.PostAsync($"/api/Pot/{potId}/invites", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    public static Task<HttpResponseMessage> RevokeInviteAsync(HttpClient client, Guid potId, Guid inviteId) =>
        client.PostAsync($"/api/Pot/{potId}/invites/{inviteId}/revoke", null);

    public static Task<HttpResponseMessage> JoinAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/Pot/join", new { code });

    /// <summary>Joins and asserts it worked — the common path, so tests do not repeat the check.</summary>
    public static async Task JoinExpectingSuccessAsync(HttpClient client, string code)
    {
        var response = await JoinAsync(client, code);
        Assert.True(response.IsSuccessStatusCode,
            $"join failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    public static Task<HttpResponseMessage> LeaveAsync(HttpClient client, Guid potId) =>
        client.PostAsync($"/api/Pot/{potId}/leave", null);

    public static Task<HttpResponseMessage> GetPotAsync(HttpClient client, Guid potId) =>
        client.GetAsync($"/api/Pot/{potId}");

    public static async Task<IReadOnlyList<JsonElement>> MembersAsync(HttpClient client, Guid potId)
    {
        var response = await client.GetAsync($"/api/Pot/{potId}/members");
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).EnumerateArray().ToList();
    }

    public static async Task<IReadOnlyList<string>> MemberNamesAsync(HttpClient client, Guid potId) =>
        (await MembersAsync(client, potId))
            .Where(m => m.GetProperty("isActive").GetBoolean())
            .Select(m => m.GetProperty("displayName").GetString()!)
            .ToList();

    public static async Task<IReadOnlyList<JsonElement>> ActivityAsync(HttpClient client, Guid potId)
    {
        var response = await client.GetAsync($"/api/Pot/{potId}/activity");
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).EnumerateArray().ToList();
    }

    /// <summary>Activity kinds newest-first, as the enum's integer values. See PotActivityKind.</summary>
    public static async Task<IReadOnlyList<int>> ActivityKindsAsync(HttpClient client, Guid potId) =>
        (await ActivityAsync(client, potId)).Select(a => a.GetProperty("kind").GetInt32()).ToList();

    public static async Task<IReadOnlyList<JsonElement>> InvitesAsync(HttpClient client, Guid potId)
    {
        var response = await client.GetAsync($"/api/Pot/{potId}/invites");
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).EnumerateArray().ToList();
    }

    public static async Task<IReadOnlyList<JsonElement>> MyPotsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/Pot");
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).EnumerateArray().ToList();
    }

    // ── The cross-user assertion this suite could not previously write ─────────

    /// <summary>
    /// Every read route a pot has, driven as <paramref name="stranger"/>.
    /// <para>
    /// Asserting 404 rather than 403 is the point, and it is asserted on the
    /// whole surface rather than on one representative route: an endpoint that
    /// forgets its membership check does not announce itself, and a single 403
    /// anywhere here would confirm to an outsider that the pot exists.
    /// </para>
    /// </summary>
    public static async Task AssertInvisibleToAsync(HttpClient stranger, Guid potId, Guid? inviteId = null)
    {
        var reads = new[]
        {
            $"/api/Pot/{potId}",
            $"/api/Pot/{potId}/members",
            $"/api/Pot/{potId}/invites",
            $"/api/Pot/{potId}/activity",
        };

        foreach (var route in reads)
        {
            var response = await stranger.GetAsync(route);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                $"GET {route} answered {(int)response.StatusCode} — a non-member must get 404, never 403.");
        }

        var writes = new List<(string Route, Func<Task<HttpResponseMessage>> Send)>
        {
            ($"POST /api/Pot/{potId}/invites", () => stranger.PostAsync($"/api/Pot/{potId}/invites", null)),
            ($"POST /api/Pot/{potId}/leave", () => stranger.PostAsync($"/api/Pot/{potId}/leave", null)),
            ($"PUT /api/Pot/{potId}", () => stranger.PutAsJsonAsync($"/api/Pot/{potId}",
                new { name = "Renamed by a stranger", lowBalanceThreshold = 0m })),
        };

        if (inviteId is Guid invite)
            writes.Add(($"POST /api/Pot/{potId}/invites/{invite}/revoke",
                () => stranger.PostAsync($"/api/Pot/{potId}/invites/{invite}/revoke", null)));

        foreach (var (route, send) in writes)
        {
            var response = await send();
            Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                $"{route} answered {(int)response.StatusCode} — a non-member must get 404, never 403.");
        }
    }

    // ── W2, the ledger ─────────────────────────────────────────────────────────
    //
    // Every write answers with the whole fresh ledger, so these return it and
    // the tests read their assertions straight off the response rather than
    // re-fetching. That is not only convenience: it is how a test can tell that
    // the numbers a member sees immediately after acting are the right ones.

    public static async Task<JsonElement> LedgerAsync(HttpClient client, Guid potId)
    {
        var response = await client.GetAsync($"/api/Pot/{potId}/ledger");
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }

    public static decimal BalanceOf(JsonElement ledger) =>
        ledger.GetProperty("balance").GetDecimal();

    /// <summary>Every member's position, keyed by display name — which is how a test knows who is who.</summary>
    public static IReadOnlyDictionary<string, decimal> PositionsOf(JsonElement ledger) =>
        ledger.GetProperty("positions")
              .EnumerateArray()
              .ToDictionary(
                  p => p.GetProperty("displayName").GetString()!,
                  p => p.GetProperty("position").GetDecimal());

    public static IReadOnlyList<JsonElement> EntriesOf(JsonElement ledger) =>
        ledger.GetProperty("entries").EnumerateArray().ToList();

    public static Task<HttpResponseMessage> TopUpAsync(
        HttpClient client, Guid potId, decimal amount, Guid sourceAccountId,
        string? date = null, string? note = null) =>
        client.PostAsJsonAsync($"/api/Pot/{potId}/topups", new { amount, sourceAccountId, date, note });

    public static async Task<JsonElement> TopUpExpectingSuccessAsync(
        HttpClient client, Guid potId, decimal amount, Guid sourceAccountId,
        string? date = null, string? note = null)
    {
        var response = await TopUpAsync(client, potId, amount, sourceAccountId, date, note);
        Assert.True(response.IsSuccessStatusCode,
            $"top-up failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await ReadAsync(response);
    }

    /// <summary>
    /// A pot expense. <paramref name="shares"/> is one entry per participant:
    /// a null amount means "take an even slice of what is left", which is what
    /// "everyone", "a subset" and "just one person" all send.
    /// </summary>
    public static Task<HttpResponseMessage> LogExpenseAsync(
        HttpClient client, Guid potId, decimal amount,
        IEnumerable<(Guid MembershipId, decimal? Amount)> shares,
        string title = "Groceries", string? date = null, Guid? paidFromAccountId = null) =>
        client.PostAsJsonAsync($"/api/Pot/{potId}/expenses", new
        {
            title,
            amount,
            date,
            paidFromAccountId,
            shares = shares.Select(s => new { membershipId = s.MembershipId, amount = s.Amount }).ToList()
        });

    public static async Task<JsonElement> LogExpenseExpectingSuccessAsync(
        HttpClient client, Guid potId, decimal amount,
        IEnumerable<(Guid MembershipId, decimal? Amount)> shares,
        string title = "Groceries", string? date = null, Guid? paidFromAccountId = null)
    {
        var response = await LogExpenseAsync(client, potId, amount, shares, title, date, paidFromAccountId);
        Assert.True(response.IsSuccessStatusCode,
            $"expense failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await ReadAsync(response);
    }

    /// <summary>The common shape: split evenly across everybody named.</summary>
    public static IEnumerable<(Guid, decimal?)> Evenly(params Guid[] membershipIds) =>
        membershipIds.Select(id => (id, (decimal?)null));

    public static Task<HttpResponseMessage> DeleteExpenseAsync(HttpClient client, Guid potId, Guid expenseId) =>
        client.DeleteAsync($"/api/Pot/{potId}/expenses/{expenseId}");

    public static Task<HttpResponseMessage> WithdrawAsync(
        HttpClient client, Guid potId, decimal amount, Guid destinationAccountId, string? note = null) =>
        client.PostAsJsonAsync($"/api/Pot/{potId}/withdrawals",
            new { amount, destinationAccountId, note });

    public static async Task<JsonElement> WithdrawExpectingSuccessAsync(
        HttpClient client, Guid potId, decimal amount, Guid destinationAccountId, string? note = null)
    {
        var response = await WithdrawAsync(client, potId, amount, destinationAccountId, note);
        Assert.True(response.IsSuccessStatusCode,
            $"withdrawal failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await ReadAsync(response);
    }

    /// <summary>
    /// A member's own membership id in a pot — what the attribution picker sends
    /// and what every share is keyed on.
    /// </summary>
    public static async Task<Guid> MyMembershipIdAsync(HttpClient client, Guid potId) =>
        (await ReadAsync(await GetPotAsync(client, potId))).GetProperty("membershipId").GetGuid();

    /// <summary>
    /// A funded account for a member, in the pot's currency, so top-ups have
    /// somewhere to come from. Returns the account id.
    /// </summary>
    public static async Task<Guid> FundedAccountAsync(
        HttpClient client, decimal balance = 10_000m, string currencyCode = "EUR")
    {
        var currencyId = await ApiClient.CreateCurrencyAsync(client, currencyCode);
        return await ApiClient.CreateAccountAsync(client, currencyId, balance, name: $"{currencyCode} account");
    }

    /// <summary>
    /// <b>The assertion this whole wave exists for.</b>
    ///
    /// <para>
    /// <c>Σ Bᵢ ≡ pot cash balance</c> — every member's position added together
    /// is exactly the money in the pot. It holds because out-of-pocket payments
    /// cancel (a contribution and a consumption in one gesture) and because
    /// every expense's shares sum to the expense to the cent. If it ever drifts,
    /// something double-counted, and the positions have stopped describing the
    /// pot's real money.
    /// </para>
    ///
    /// <para>
    /// Asserted with <c>Assert.Equal</c> on decimals and no tolerance, on
    /// purpose. A tolerance would be admitting the arithmetic is approximate,
    /// and it is not: the allocator works in integer cents precisely so that
    /// this comparison can be exact.
    /// </para>
    /// </summary>
    public static void AssertInvariant(JsonElement ledger, string context = "")
    {
        var balance = BalanceOf(ledger);
        var positions = ledger.GetProperty("positions")
                              .EnumerateArray()
                              .Sum(p => p.GetProperty("position").GetDecimal());

        Assert.True(balance == positions,
            $"Σ Bᵢ = {positions} but the pot holds {balance}{(context.Length > 0 ? $" — {context}" : "")}");
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>The <c>message</c> a 400 carries, for tests that assert the sentence and not just the code.</summary>
    public static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        if (body.TryGetProperty("message", out var message)) return message.GetString() ?? string.Empty;
        if (body.TryGetProperty("detail", out var detail)) return detail.GetString() ?? string.Empty;
        return body.ToString();
    }
}
