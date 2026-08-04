using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// W1 asserted that joining changed nothing whatsoever, and named the one
    /// thing that would change it: "the reserved 'Shared' category that does
    /// appear on first join is W2's". This is that wave, so the pin narrows to
    /// exactly that — one category appears, and not one number moves.
    /// </summary>
    [Fact]
    public async Task Joining_a_pot_adds_the_shared_category_and_moves_no_number()
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

        var after = await SnapshotAsync(mara.Client);

        // Everything is still byte-identical apart from the two payloads that
        // enumerate categories — the list itself, and the JSON export that
        // carries a copy of it. Those are exempted here and asserted precisely
        // below and in the next test; a blanket exemption would let a real
        // change hide behind it. The CSV export is *not* exempt: it is
        // transactions, and an empty category has none.
        foreach (var (route, body) in before)
        {
            if (route == "/api/Category") continue;

            var (left, right) = route == "/api/Export/json"
                ? (WithoutCategories(body), WithoutCategories(after[route]))
                : (body, after[route]);

            Assert.True(left == right,
                $"{route} changed.\nBefore: {left}\nAfter:  {right}");
        }

        // The export gained the category and nothing else — the same one row,
        // and no money anywhere in it moved.
        Assert.Contains("\"Shared\"", after["/api/Export/json"]);
        Assert.DoesNotContain("\"Shared\"", before["/api/Export/json"]);

        // And the category list gained exactly one row, which is the reserved
        // one, with nothing in it yet.
        var categories = await CategoriesAsync(mara.Client);
        var shared = Assert.Single(categories, c => c.TryGetProperty("systemKind", out _));

        Assert.Equal("Shared", shared.GetProperty("name").GetString());
        Assert.Equal(0m, shared.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(
            JsonSerializer.Deserialize<JsonElement>(before["/api/Category"]).GetArrayLength() + 1,
            categories.Count);
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

    /// <summary>
    /// The other half of the same rule, read from the side that did not change:
    /// the eleven starter categories are still exactly the eleven starter
    /// categories, byte for byte, and none of them has learned that this user is
    /// in a pot. Only the twelfth row is new.
    /// </summary>
    [Fact]
    public async Task The_starter_category_list_is_untouched_and_carries_no_system_mark()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var before = await CategoriesAsync(mara.Client);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        var after = await CategoriesAsync(mara.Client);

        // A category the user made carries no systemKind at all — the field is
        // omitted rather than serialised as null, which is what keeps a user in
        // no pots seeing the payload they saw before this wave.
        foreach (var category in before)
        {
            Assert.False(category.TryGetProperty("systemKind", out _));

            var same = Assert.Single(after,
                c => c.GetProperty("id").GetGuid() == category.GetProperty("id").GetGuid());

            Assert.Equal(category.ToString(), same.ToString());
        }

        Assert.Equal(before.Count + 1, after.Count);
    }

    /// <summary>
    /// The JSON export with its category catalogue cut out, so the rest of it —
    /// every account, every transaction, every figure — can still be compared
    /// byte for byte across a join that legitimately adds one category.
    /// </summary>
    private static string WithoutCategories(string exportJson)
    {
        var node = JsonNode.Parse(exportJson)!.AsObject();
        node.Remove("categories");
        return node.ToJsonString();
    }

    private static async Task<IReadOnlyList<JsonElement>> CategoriesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/Category");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }
}
