using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

/// <summary>
/// W1's fifth acceptance item, and the cycle's second rule: <b>a user in no pots
/// sees an unchanged app</b> — category list, dashboard and export identical to
/// pre-W1, to the cent.
///
/// <para>
/// This is the third layer of adjustment on money math that R and V already
/// rewrote in the same week, which is exactly why it is pinned rather than
/// assumed. The pins compare a user's own payloads across an interval in which
/// pots are created, joined and left all around them: nothing about somebody
/// else's group may move a byte of what this user sees. W2 and W3 tighten the
/// same screw as they add money to it.
/// </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PotNeutralityTests
{
    private readonly CustomWebApplicationFactory _factory;

    public PotNeutralityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Everything a user sees that could conceivably learn about pots. The
    /// dashboard reads at the end are the ones W4 will eventually change; W1
    /// must not.
    /// </summary>
    private static readonly string[] SurfacesUnderPin =
    {
        "/api/Category",
        "/api/Currency",
        "/api/Account",
        "/api/Expense",
        "/api/Income",
        "/api/Budget",
        "/api/SavingGoal",
        "/api/Reimbursement",
        "/api/Transfer",
        "/api/Insights",
        "/api/Cashflow/projection",
        "/api/Export/json",
        "/api/Export/csv",
    };

    /// <summary>
    /// The export stamps the moment it was taken, which is a fact about the
    /// request rather than about the data. It is the only field in any of these
    /// payloads that moves on its own, so it is normalised and everything else
    /// is compared byte for byte — blanket-normalising timestamps would let a
    /// real change hide behind the exemption.
    /// </summary>
    private static readonly Regex ExportedAt =
        new("\"exportedAtUtc\": \"[^\"]*\"", RegexOptions.Compiled);

    private static async Task<Dictionary<string, string>> SnapshotAsync(HttpClient client)
    {
        var snapshot = new Dictionary<string, string>(SurfacesUnderPin.Length);

        foreach (var route in SurfacesUnderPin)
        {
            var response = await client.GetAsync(route);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            snapshot[route] = ExportedAt.Replace(body, "\"exportedAtUtc\": \"<when>\"");
        }

        return snapshot;
    }

    private static void AssertIdentical(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        foreach (var (route, body) in before)
            Assert.True(body == after[route],
                $"{route} changed.\nBefore: {body}\nAfter:  {after[route]}");
    }

    [Fact]
    public async Task A_user_in_no_pots_sees_byte_identical_responses_while_pots_happen_around_them()
    {
        var users = await PotFixture.UsersAsync(_factory, 3);
        var (bystander, alp, mara) = (users[0], users[1], users[2]);

        // An ordinary user with ordinary data, who will never touch a pot.
        var currencyId = await ApiClient.CreateCurrencyAsync(bystander.Client, "EUR");
        var accountId = await ApiClient.CreateAccountAsync(bystander.Client, currencyId, 1_000m);
        var categoryId = await ApiClient.CreateCategoryAsync(bystander.Client, categoryType: 0);
        (await bystander.Client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Coffee",
            amount = 25m,
            date = "2026-08-01",
            accountId,
            categoryId
        })).EnsureSuccessStatusCode();

        var before = await SnapshotAsync(bystander.Client);

        // Meanwhile, the whole of W1 happens to other people: a pot is created,
        // invited into, joined, renamed, left, and rejoined.
        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var code = await PotFixture.InviteCodeAsync(alp.Client, potId);
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);
        (await alp.Client.PutAsJsonAsync($"/api/Pot/{potId}",
            new { name = "The Flat", lowBalanceThreshold = 25m })).EnsureSuccessStatusCode();
        (await PotFixture.LeaveAsync(mara.Client, potId)).EnsureSuccessStatusCode();
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);

        AssertIdentical(before, await SnapshotAsync(bystander.Client));
    }

    [Fact]
    public async Task Joining_a_pot_changes_none_of_your_own_numbers()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var currencyId = await ApiClient.CreateCurrencyAsync(mara.Client, "EUR");
        var accountId = await ApiClient.CreateAccountAsync(mara.Client, currencyId, 500m);
        var categoryId = await ApiClient.CreateCategoryAsync(mara.Client, categoryType: 0);
        (await mara.Client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Groceries",
            amount = 40m,
            date = "2026-08-01",
            accountId,
            categoryId
        })).EnsureSuccessStatusCode();

        var before = await SnapshotAsync(mara.Client);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        // A pot with no money in it moves nothing personal — not the balance,
        // not the budget, and in particular not the category list. The reserved
        // "Shared" category that does appear on first join is W2's, and a member
        // seeing it a wave early would mean W2 leaked backwards.
        AssertIdentical(before, await SnapshotAsync(mara.Client));
    }

    [Fact]
    public async Task No_personal_surface_mentions_pots_at_all()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        var snapshot = await SnapshotAsync(mara.Client);

        // Read the other way round from the byte comparison above: not "did it
        // change" but "is the concept present". W4 is where the export and the
        // dashboard learn about pots, deliberately and with its own pins.
        foreach (var (route, body) in snapshot)
        {
            Assert.DoesNotContain("\"pot", body, StringComparison.OrdinalIgnoreCase);
            Assert.False(body.Contains("Flat", StringComparison.Ordinal),
                $"{route} mentions the pot's name.");
        }
    }

    [Fact]
    public async Task The_starter_category_list_is_untouched_by_this_wave()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var before = await mara.Client.GetStringAsync("/api/Category");

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        Assert.Equal(before, await mara.Client.GetStringAsync("/api/Category"));
    }
}
