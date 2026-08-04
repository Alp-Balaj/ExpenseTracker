using System.Net;
using ExpenseTracker.Domain.Entities.Pots;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Shared.Enums;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

/// <summary>
/// W1's second acceptance item. Joining is opt-in and revocable, and the three
/// ways a link can be refused are told apart — because "ask for a new one" and
/// "that was cancelled" are different next actions for the person holding it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PotInviteTests
{
    private readonly CustomWebApplicationFactory _factory;

    public PotInviteTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task A_revoked_invite_refuses_redemption_with_a_plain_message()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var invite = await PotFixture.InviteAsync(alp.Client, potId);
        var code = invite.GetProperty("code").GetString()!;

        (await PotFixture.RevokeInviteAsync(alp.Client, potId, invite.GetProperty("id").GetGuid()))
            .EnsureSuccessStatusCode();

        var response = await PotFixture.JoinAsync(mara.Client, code);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("That invite has been revoked.", await PotFixture.MessageAsync(response));

        // Refused means refused: no membership was written on the way past.
        Assert.Single(await PotFixture.MemberNamesAsync(alp.Client, potId));
        await PotFixture.AssertInvisibleToAsync(mara.Client, potId);
    }

    [Fact]
    public async Task An_expired_invite_refuses_redemption_with_a_plain_message()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var code = await PotFixture.InviteCodeAsync(alp.Client, potId);

        // Expiry is a clock fact, and there is no endpoint that moves the clock.
        // Reaching into the context here is the same latitude UserDeletionCascadeTests
        // takes: the thing under test is what the app does once the row says
        // that, not how the row came to say it.
        await BackdateExpiryAsync(code);

        var response = await PotFixture.JoinAsync(mara.Client, code);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("That invite has expired. Ask a member for a new link.", await PotFixture.MessageAsync(response));

        Assert.Single(await PotFixture.MemberNamesAsync(alp.Client, potId));
    }

    [Fact]
    public async Task Redeeming_a_valid_link_twice_yields_exactly_one_membership()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var code = await PotFixture.InviteCodeAsync(alp.Client, potId);

        // The double tap, the retried request, the link opened again from the
        // chat. All the same thing.
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);

        var members = await PotFixture.MemberNamesAsync(alp.Client, potId);
        Assert.Equal(new[] { alp.DisplayName, mara.DisplayName }, members);

        // And the log says she joined once — a replay that produced three lines
        // would be a different kind of wrong from a duplicate row, and just as
        // visible to the group.
        var joins = (await PotFixture.ActivityKindsAsync(alp.Client, potId))
            .Count(k => k == (int)PotActivityKind.MemberJoined);
        Assert.Equal(1, joins);
    }

    [Fact]
    public async Task An_unknown_code_is_refused_without_saying_whether_a_pot_exists()
    {
        var (_, mara) = await PotFixture.TwoUsersAsync(_factory);

        var response = await PotFixture.JoinAsync(mara.Client, "ZZZZZZZZZZZZZZZZZZZZZZ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("That invite link is not valid.", await PotFixture.MessageAsync(response));
    }

    [Fact]
    public async Task Any_member_may_revoke_any_invite_including_one_they_did_not_issue()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        // Alp issues; Mara kills it. Flat trust, and the log is what makes that
        // safe rather than a permission check.
        var alpsInvite = await PotFixture.InviteAsync(alp.Client, potId);
        var response = await PotFixture.RevokeInviteAsync(
            mara.Client, potId, alpsInvite.GetProperty("id").GetGuid());
        response.EnsureSuccessStatusCode();

        var revoked = await PotFixture.ReadAsync(response);
        Assert.False(revoked.GetProperty("isRedeemable").GetBoolean());
        Assert.Equal(mara.DisplayName, revoked.GetProperty("revokedByDisplayName").GetString());
        Assert.Equal(alp.DisplayName, revoked.GetProperty("issuedByDisplayName").GetString());

        Assert.Contains((int)PotActivityKind.InviteRevoked, await PotFixture.ActivityKindsAsync(alp.Client, potId));
    }

    [Fact]
    public async Task A_link_says_what_it_is_for_before_you_accept_it_and_no_more()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var currencyId = await ApiClient.CreateCurrencyAsync(alp.Client, "EUR");
        await ApiClient.CreateAccountAsync(alp.Client, currencyId, 1_000m, name: "Alp's secret account");

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var code = await PotFixture.InviteCodeAsync(alp.Client, potId);

        // The one route that answers for a non-member — that is what an invite
        // is. Three fields: what the pot is called, what it is denominated in,
        // and who asked you.
        var response = await mara.Client.GetAsync($"/api/Pot/invites/{code}");
        response.EnsureSuccessStatusCode();

        var preview = await PotFixture.ReadAsync(response);
        Assert.Equal("Flat", preview.GetProperty("potName").GetString());
        Assert.Equal("EUR", preview.GetProperty("currencyCode").GetString());
        Assert.Equal(alp.DisplayName, preview.GetProperty("invitedByDisplayName").GetString());
        Assert.True(preview.GetProperty("isRedeemable").GetBoolean());
        Assert.False(preview.GetProperty("alreadyAMember").GetBoolean());

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Alp's secret account", raw);
        Assert.DoesNotContain(alp.UserId, raw);
        Assert.DoesNotContain(potId.ToString(), raw);
    }

    [Fact]
    public async Task A_preview_of_a_dead_link_says_which_kind_of_dead()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var invite = await PotFixture.InviteAsync(alp.Client, potId);
        (await PotFixture.RevokeInviteAsync(alp.Client, potId, invite.GetProperty("id").GetGuid()))
            .EnsureSuccessStatusCode();

        var response = await mara.Client.GetAsync($"/api/Pot/invites/{invite.GetProperty("code").GetString()}");
        response.EnsureSuccessStatusCode();

        var preview = await PotFixture.ReadAsync(response);
        Assert.False(preview.GetProperty("isRedeemable").GetBoolean());
        Assert.Equal("That invite has been revoked.", preview.GetProperty("problem").GetString());
    }

    [Fact]
    public async Task Invite_codes_are_not_guessable_and_do_not_repeat()
    {
        var (alp, _) = await PotFixture.TwoUsersAsync(_factory);
        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");

        var codes = new List<string>();
        for (var i = 0; i < 10; i++)
            codes.Add(await PotFixture.InviteCodeAsync(alp.Client, potId));

        Assert.Equal(10, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Equal(22, code.Length));

        // The code is the whole guard on the one unscoped read in the feature,
        // so a short or low-alphabet code is a real vulnerability rather than an
        // aesthetic complaint.
        Assert.All(codes, code => Assert.Matches("^[A-HJ-NP-Z2-9]{22}$", code));
    }

    [Fact]
    public async Task An_invite_code_never_appears_in_the_activity_log()
    {
        var (alp, _) = await PotFixture.TwoUsersAsync(_factory);
        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var code = await PotFixture.InviteCodeAsync(alp.Client, potId);

        // Every member can read the log forever. A revoked invite whose code was
        // still sitting in it would not be revoked in any sense that matters.
        var raw = string.Join("\n",
            (await PotFixture.ActivityAsync(alp.Client, potId)).Select(a => a.ToString()));

        Assert.DoesNotContain(code, raw);
    }

    private async Task BackdateExpiryAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var invite = await db.Set<PotInvite>().SingleAsync(i => i.Code == code);
        invite.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }
}
