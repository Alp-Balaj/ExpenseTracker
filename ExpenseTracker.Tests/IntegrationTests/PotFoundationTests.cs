using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Shared.Enums;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

/// <summary>
/// Cycle U W1's first acceptance item, and the two-user assertions that come
/// with it: a group exists, two people are in it, and a third cannot tell that
/// it does.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PotFoundationTests
{
    private readonly CustomWebApplicationFactory _factory;

    public PotFoundationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task One_user_creates_a_pot_another_joins_by_link_and_both_see_each_other()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var code = await PotFixture.InviteCodeAsync(alp.Client, potId);

        // Before redeeming, Mara is a stranger to this pot in every direction.
        await PotFixture.AssertInvisibleToAsync(mara.Client, potId);

        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);

        // Each of them reads the pot through their own token, and sees both
        // names. This is the assertion the suite had no fixture for before W1.
        Assert.Equal(
            new[] { alp.DisplayName, mara.DisplayName },
            await PotFixture.MemberNamesAsync(alp.Client, potId));

        Assert.Equal(
            new[] { alp.DisplayName, mara.DisplayName },
            await PotFixture.MemberNamesAsync(mara.Client, potId));

        // And each sees exactly themselves marked as "you".
        foreach (var (client, name) in new[] { (alp.Client, alp.DisplayName), (mara.Client, mara.DisplayName) })
        {
            var you = Assert.Single(
                await PotFixture.MembersAsync(client, potId),
                m => m.GetProperty("isYou").GetBoolean());

            Assert.Equal(name, you.GetProperty("displayName").GetString());
        }
    }

    [Fact]
    public async Task A_third_user_gets_404_on_every_pot_endpoint()
    {
        var users = await PotFixture.UsersAsync(_factory, 3);
        var (alp, mara, tomas) = (users[0], users[1], users[2]);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var invite = await PotFixture.InviteAsync(alp.Client, potId);
        var inviteId = invite.GetProperty("id").GetGuid();
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, invite.GetProperty("code").GetString()!);

        // Tomas is a real, signed-in user with pots of his own — the check under
        // test is membership, not authentication.
        await PotFixture.CreatePotIdAsync(tomas.Client, "Tomas's own pot");

        await PotFixture.AssertInvisibleToAsync(tomas.Client, potId, inviteId);

        // And the pot does not appear in his list, which is the same rule read
        // from the other end.
        Assert.DoesNotContain(
            await PotFixture.MyPotsAsync(tomas.Client),
            p => p.GetProperty("id").GetGuid() == potId);
    }

    [Fact]
    public async Task A_pot_endpoint_answers_the_same_way_for_a_pot_that_never_existed()
    {
        var (_, mara) = await PotFixture.TwoUsersAsync(_factory);

        // The whole reason for 404-not-403: "someone else's pot" and "no such
        // pot" have to be indistinguishable, or the status code itself is a
        // disclosure.
        var response = await PotFixture.GetPotAsync(mara.Client, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_pot_is_EUR_with_a_50_low_balance_default()
    {
        var (alp, _) = await PotFixture.TwoUsersAsync(_factory);

        var pot = await PotFixture.CreatePotAsync(alp.Client, "Flat");

        // W0's two decisions, pinned. EUR because it matches every member's base
        // currency, so nobody sees one figure on the dashboard and another in
        // the pot; 50 because W4's alert has to actually fire against seeded
        // spending.
        Assert.Equal("EUR", pot.GetProperty("currencyCode").GetString());
        Assert.Equal(50m, pot.GetProperty("lowBalanceThreshold").GetDecimal());
        Assert.Equal(1, pot.GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task A_pot_holds_no_money_in_this_wave()
    {
        var (alp, _) = await PotFixture.TwoUsersAsync(_factory);
        var pot = await PotFixture.CreatePotAsync(alp.Client, "Flat");

        // W1 ships the group and nothing else. If one of these ever appears it
        // arrived from W2, and it arrived without the Σ Bᵢ invariant that is
        // supposed to come with it.
        foreach (var absent in new[] { "balance", "position", "positions", "shares", "topUps", "withdrawals" })
            Assert.False(pot.TryGetProperty(absent, out _), $"W1's pot must not carry '{absent}'.");
    }

    [Fact]
    public async Task Any_member_can_rename_the_pot_and_move_the_low_balance_line()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        // Flat trust: the person who did not create the pot changes it, and the
        // creator is not consulted.
        var response = await mara.Client.PutAsJsonAsync($"/api/Pot/{potId}",
            new { name = "The Flat", lowBalanceThreshold = 25m });
        response.EnsureSuccessStatusCode();

        var seenByCreator = await PotFixture.ReadAsync(await PotFixture.GetPotAsync(alp.Client, potId));
        Assert.Equal("The Flat", seenByCreator.GetProperty("name").GetString());
        Assert.Equal(25m, seenByCreator.GetProperty("lowBalanceThreshold").GetDecimal());

        // Two separate log lines, because they are two separate things a member
        // reading the log would want to see.
        var kinds = await PotFixture.ActivityKindsAsync(alp.Client, potId);
        Assert.Contains((int)PotActivityKind.PotRenamed, kinds);
        Assert.Contains((int)PotActivityKind.LowBalanceThresholdChanged, kinds);
    }

    [Fact]
    public async Task The_currency_is_fixed_at_creation()
    {
        var (alp, _) = await PotFixture.TwoUsersAsync(_factory);
        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Trip", currencyCode: "EUR");

        // There is no field for it on the update DTO, and a client that sends
        // one anyway must not move it — changing a pot's currency would silently
        // reinterpret every amount W2 goes on to write.
        var response = await alp.Client.PutAsJsonAsync($"/api/Pot/{potId}",
            new { name = "Trip", lowBalanceThreshold = 50m, currencyCode = "USD" });
        response.EnsureSuccessStatusCode();

        var pot = await PotFixture.ReadAsync(await PotFixture.GetPotAsync(alp.Client, potId));
        Assert.Equal("EUR", pot.GetProperty("currencyCode").GetString());
    }

    [Fact]
    public async Task A_member_belongs_to_any_number_of_pots()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var household = await PotFixture.CreatePotIdAsync(alp.Client, "Household");
        var trip = await PotFixture.CreatePotIdAsync(mara.Client, "Trip");
        await PotFixture.JoinExpectingSuccessAsync(
            alp.Client, await PotFixture.InviteCodeAsync(mara.Client, trip));

        var mine = await PotFixture.MyPotsAsync(alp.Client);
        Assert.Contains(mine, p => p.GetProperty("id").GetGuid() == household);
        Assert.Contains(mine, p => p.GetProperty("id").GetGuid() == trip);

        // Mara is in the trip and not in the household — one member's pot list
        // is not the other's.
        var hers = await PotFixture.MyPotsAsync(mara.Client);
        Assert.Contains(hers, p => p.GetProperty("id").GetGuid() == trip);
        Assert.DoesNotContain(hers, p => p.GetProperty("id").GetGuid() == household);
    }

    [Fact]
    public async Task A_pot_endpoint_never_discloses_a_members_accounts_or_categories()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var currencyId = await ApiClient.CreateCurrencyAsync(alp.Client, "EUR");
        await ApiClient.CreateAccountAsync(alp.Client, currencyId, 1_000m, name: "Alp's secret account");
        await ApiClient.CreateCategoryAsync(alp.Client, name: "Alp's secret category");

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        // Everything a member may read about a pot, concatenated. Privacy here
        // is "members see pot activity only" — not the accounts behind it.
        var everything = string.Join("\n", new[]
        {
            (await PotFixture.GetPotAsync(mara.Client, potId)) is var p ? await p.Content.ReadAsStringAsync() : "",
            await (await mara.Client.GetAsync($"/api/Pot/{potId}/members")).Content.ReadAsStringAsync(),
            await (await mara.Client.GetAsync($"/api/Pot/{potId}/invites")).Content.ReadAsStringAsync(),
            await (await mara.Client.GetAsync($"/api/Pot/{potId}/activity")).Content.ReadAsStringAsync(),
        });

        Assert.DoesNotContain("Alp's secret account", everything);
        Assert.DoesNotContain("Alp's secret category", everything);

        // Nor the user id behind a member: inside a pot a person is their frozen
        // label and nothing else.
        Assert.DoesNotContain(alp.UserId, everything);
        Assert.DoesNotContain(alp.Email, everything);
    }
}
