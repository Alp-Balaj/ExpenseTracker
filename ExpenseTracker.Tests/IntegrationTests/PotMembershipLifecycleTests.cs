using System.Net;
using System.Net.Http.Json;
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
/// W1's third and fourth acceptance items: leaving, and the deletion story.
/// <para>
/// The deletion half is the one that would have been discovered as a bug rather
/// than as design. Every other table in this database cascades from
/// <c>AspNetUsers</c>, so before W1 a pot creator deleting their account would
/// have taken the group's history with them. <c>PotMembership.UserId</c> is
/// nullable with <c>OnDelete(Restrict)</c> precisely so that cannot happen, and
/// so that a code path which forgets to anonymize fails loudly instead of
/// quietly succeeding.
/// </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PotMembershipLifecycleTests
{
    private const string Password = "P@ssw0rd-123!";

    private readonly CustomWebApplicationFactory _factory;

    public PotMembershipLifecycleTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Leaving_removes_the_member_from_the_list_and_leaves_the_log_intact()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        var before = await PotFixture.ActivityAsync(alp.Client, potId);

        var response = await PotFixture.LeaveAsync(mara.Client, potId);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Gone from the member list...
        Assert.Equal(new[] { alp.DisplayName }, await PotFixture.MemberNamesAsync(alp.Client, potId));

        // ...and the pot is invisible to her again, immediately.
        await PotFixture.AssertInvisibleToAsync(mara.Client, potId);

        // But nothing she did was erased, and the departure itself is on the
        // record. This is why leaving stamps a row rather than deleting one.
        var after = await PotFixture.ActivityAsync(alp.Client, potId);
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal((int)PotActivityKind.MemberLeft, after[0].GetProperty("kind").GetInt32());
        Assert.Equal(mara.DisplayName, after[0].GetProperty("actorDisplayName").GetString());

        // She is still nameable in the history, as a former member.
        var everyone = await PotFixture.MembersAsync(alp.Client, potId);
        var departed = Assert.Single(everyone, m => !m.GetProperty("isActive").GetBoolean());
        Assert.Equal(mara.DisplayName, departed.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Rejoining_reuses_the_original_membership_and_keeps_join_order()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var code = await PotFixture.InviteCodeAsync(alp.Client, potId);

        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);
        var original = (await PotFixture.MembersAsync(alp.Client, potId))
            .Single(m => m.GetProperty("displayName").GetString() == mara.DisplayName);
        var originalId = original.GetProperty("id").GetGuid();
        var originalJoinedAt = original.GetProperty("joinedAt").GetDateTime();

        (await PotFixture.LeaveAsync(mara.Client, potId)).EnsureSuccessStatusCode();
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);

        var members = await PotFixture.MembersAsync(alp.Client, potId);
        var rejoined = Assert.Single(members, m => m.GetProperty("displayName").GetString() == mara.DisplayName);

        // Same row, same joined-at. W2's largest-remainder tie-break is ordered
        // on this timestamp, so a member who stepped out and came back must not
        // shuffle to the back of the queue — and there must not be two of her.
        Assert.Equal(originalId, rejoined.GetProperty("id").GetGuid());
        Assert.Equal(originalJoinedAt, rejoined.GetProperty("joinedAt").GetDateTime());
        Assert.Equal(2, members.Count);
    }

    [Fact]
    public async Task Deleting_an_account_anonymizes_its_memberships_and_the_pot_survives()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        // Mara creates it, so the pot's creator is the one who leaves the app —
        // the case that would otherwise take the whole group's history.
        var potId = await PotFixture.CreatePotIdAsync(mara.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            alp.Client, await PotFixture.InviteCodeAsync(mara.Client, potId));

        var response = await mara.Client.PostAsJsonAsync("/api/User/delete-account", new { password = Password });
        response.EnsureSuccessStatusCode();

        // The pot is still there for the member who stayed, and still knows what
        // happened in it.
        var pot = await PotFixture.ReadAsync(await PotFixture.GetPotAsync(alp.Client, potId));
        Assert.Equal("Flat", pot.GetProperty("name").GetString());
        Assert.Equal(1, pot.GetProperty("memberCount").GetInt32());
        Assert.Null(pot.GetProperty("createdByDisplayName").ValueKind == System.Text.Json.JsonValueKind.Null
            ? null : pot.GetProperty("createdByDisplayName").GetString());

        var activity = await PotFixture.ActivityAsync(alp.Client, potId);
        Assert.Contains(activity, a => a.GetProperty("kind").GetInt32() == (int)PotActivityKind.PotCreated);
        Assert.Contains(activity, a => a.GetProperty("kind").GetInt32() == (int)PotActivityKind.MemberAnonymized);

        // Her row survives with the identity stripped out of it.
        var departed = Assert.Single(
            await PotFixture.MembersAsync(alp.Client, potId),
            m => m.GetProperty("isAnonymized").GetBoolean());

        Assert.Equal("Former member", departed.GetProperty("displayName").GetString());
        Assert.False(departed.GetProperty("isActive").GetBoolean());

        // And her name is nowhere in what is left.
        var raw = string.Join("\n", (await PotFixture.ActivityAsync(alp.Client, potId)).Select(a => a.ToString()));
        Assert.DoesNotContain(mara.UserId, raw);
    }

    [Fact]
    public async Task A_user_delete_that_skipped_the_anonymize_step_fails_at_the_foreign_key()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await db.Users.SingleAsync(u => u.Id == mara.UserId);
        db.Users.Remove(user);

        // The backstop. Every other user-owned table cascades from here; a pot
        // membership refuses, so a path that bypasses the API's anonymize step
        // cannot cascade a group's history away by accident.
        //
        // If this test ever starts failing because the exception stopped being
        // thrown, the fix is not to loosen the foreign key.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // Nothing moved.
        db.ChangeTracker.Clear();
        Assert.True(await db.Set<PotMembership>().AnyAsync(m => m.UserId == mara.UserId));
        Assert.Equal(2, (await PotFixture.MemberNamesAsync(alp.Client, potId)).Count);
    }

    [Fact]
    public async Task Deleting_an_account_with_no_pots_still_works_exactly_as_before()
    {
        var user = await ApiClient.CreateAuthenticatedUserAsync(_factory);

        // The anonymize pre-step must be a no-op for the overwhelming majority
        // of users, who are in no pots at all.
        var response = await user.Client.PostAsJsonAsync("/api/User/delete-account", new { password = Password });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == user.UserId));
    }

    [Fact]
    public async Task A_pot_a_member_left_stays_out_of_their_list()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        Assert.Contains(await PotFixture.MyPotsAsync(mara.Client), p => p.GetProperty("id").GetGuid() == potId);

        (await PotFixture.LeaveAsync(mara.Client, potId)).EnsureSuccessStatusCode();

        Assert.DoesNotContain(await PotFixture.MyPotsAsync(mara.Client), p => p.GetProperty("id").GetGuid() == potId);
    }
}
