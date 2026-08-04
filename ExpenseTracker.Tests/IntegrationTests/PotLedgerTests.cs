using System.Net;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

/// <summary>
/// Cycle U W2's acceptance list, item by item: money into a pot, out of it, and
/// the arithmetic that says whose it was.
///
/// <para>
/// Everything here drives the real HTTP API as real signed-in people, through
/// W1's <c>PotFixture</c>. The random-sequence proof of the identity lives next
/// door in <c>PotLedgerInvariantTests</c>; these are the cases the wave was
/// specified in, written so that a failure names the promise it broke.
/// </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PotLedgerTests
{
    private readonly CustomWebApplicationFactory _factory;

    public PotLedgerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Member(ApiClient.AuthenticatedUser User, Guid MembershipId, Guid AccountId)
    {
        public HttpClient Client => User.Client;
        public string Name => User.DisplayName;
    }

    /// <summary>
    /// <paramref name="count"/> members in one pot, in join order — which is the
    /// rounding tie-break — each with a funded EUR account.
    /// </summary>
    private async Task<(Guid PotId, IReadOnlyList<Member> Members)> PotOfAsync(
        int count, decimal balance = 1_000m)
    {
        var users = await PotFixture.UsersAsync(_factory, count);

        var potId = await PotFixture.CreatePotIdAsync(users[0].Client, "Flat");
        var code = await PotFixture.InviteCodeAsync(users[0].Client, potId);

        foreach (var user in users.Skip(1))
            await PotFixture.JoinExpectingSuccessAsync(user.Client, code);

        var members = new List<Member>(count);
        foreach (var user in users)
            members.Add(new Member(
                user,
                await PotFixture.MyMembershipIdAsync(user.Client, potId),
                await PotFixture.FundedAccountAsync(user.Client, balance)));

        return (potId, members);
    }

    // ── Acceptance 1 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Four_members_top_up_fifty_each_and_the_pot_reads_two_hundred()
    {
        var (potId, members) = await PotOfAsync(4);

        foreach (var member in members)
            await PotFixture.TopUpExpectingSuccessAsync(member.Client, potId, 50m, member.AccountId);

        var ledger = await PotFixture.LedgerAsync(members[0].Client, potId);

        Assert.Equal(200m, PotFixture.BalanceOf(ledger));
        Assert.All(PotFixture.PositionsOf(ledger).Values, position => Assert.Equal(50m, position));
        PotFixture.AssertInvariant(ledger);

        // And each member's own account is 50 lighter, because a top-up is real
        // money leaving a real account.
        foreach (var member in members)
            Assert.Equal(950m, await ApiClient.GetAccountBalanceAsync(member.Client, member.AccountId));
    }

    [Fact]
    public async Task Ten_split_three_ways_is_three_thirty_four_and_two_thirty_threes()
    {
        var (potId, members) = await PotOfAsync(4);

        foreach (var member in members)
            await PotFixture.TopUpExpectingSuccessAsync(member.Client, potId, 50m, member.AccountId);

        // The first three in join order, so the extra cent has a name attached
        // to it that the assertion can check.
        var three = members.Take(3).ToList();

        var ledger = await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 10m,
            PotFixture.Evenly(three.Select(m => m.MembershipId).ToArray()),
            title: "Milk and bread");

        var expense = Assert.Single(PotFixture.EntriesOf(ledger), e => e.GetProperty("kind").GetInt32() == 1);
        var shares = expense.GetProperty("shares")
                            .EnumerateArray()
                            .ToDictionary(s => s.GetProperty("displayName").GetString()!,
                                          s => s.GetProperty("amount").GetDecimal());

        Assert.Equal(3.34m, shares[three[0].Name]);
        Assert.Equal(3.33m, shares[three[1].Name]);
        Assert.Equal(3.33m, shares[three[2].Name]);
        Assert.Equal(10m, shares.Values.Sum());

        Assert.Equal(190m, PotFixture.BalanceOf(ledger));
        PotFixture.AssertInvariant(ledger);
    }

    [Fact]
    public async Task The_extra_cent_goes_to_the_earliest_joined_member_not_the_one_who_typed_it()
    {
        var (potId, members) = await PotOfAsync(4);

        foreach (var member in members)
            await PotFixture.TopUpExpectingSuccessAsync(member.Client, potId, 50m, member.AccountId);

        // Logged by the *last* member, and sent with the participants in reverse
        // order — neither of which may move the cent. The tie-break is join
        // order and nothing else, or it would shift under a member who happened
        // to be holding the phone.
        var reversed = members.Select(m => m.MembershipId).Reverse().ToArray();

        var ledger = await PotFixture.LogExpenseExpectingSuccessAsync(
            members[3].Client, potId, 10.01m, PotFixture.Evenly(reversed), title: "Four ways");

        var expense = Assert.Single(PotFixture.EntriesOf(ledger), e => e.GetProperty("kind").GetInt32() == 1);
        var shares = expense.GetProperty("shares")
                            .EnumerateArray()
                            .ToDictionary(s => s.GetProperty("displayName").GetString()!,
                                          s => s.GetProperty("amount").GetDecimal());

        // W0's own worked example: 2.51 / 2.50 / 2.50 / 2.50, the extra cent to
        // whoever joined first.
        Assert.Equal(2.51m, shares[members[0].Name]);
        Assert.Equal(2.50m, shares[members[1].Name]);
        Assert.Equal(2.50m, shares[members[2].Name]);
        Assert.Equal(2.50m, shares[members[3].Name]);
    }

    [Fact]
    public async Task A_typed_amount_pins_one_share_and_the_rest_divide_what_is_left()
    {
        var (potId, members) = await PotOfAsync(3);

        foreach (var member in members)
            await PotFixture.TopUpExpectingSuccessAsync(member.Client, potId, 100m, member.AccountId);

        // "Everyone, but Mara had the expensive one" — the custom-amounts
        // affordance, over the same share rows as every other entry mode.
        var ledger = await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 30m,
            new[]
            {
                (members[0].MembershipId, (decimal?)null),
                (members[1].MembershipId, 12m),
                (members[2].MembershipId, (decimal?)null),
            },
            title: "Dinner");

        var expense = Assert.Single(PotFixture.EntriesOf(ledger), e => e.GetProperty("kind").GetInt32() == 1);
        var shares = expense.GetProperty("shares")
                            .EnumerateArray()
                            .ToDictionary(s => s.GetProperty("displayName").GetString()!,
                                          s => s.GetProperty("amount").GetDecimal());

        Assert.Equal(12m, shares[members[1].Name]);
        Assert.Equal(9m, shares[members[0].Name]);
        Assert.Equal(9m, shares[members[2].Name]);
        Assert.Equal(30m, shares.Values.Sum());
    }

    [Fact]
    public async Task An_expense_for_one_person_puts_all_of_it_on_them()
    {
        var (potId, members) = await PotOfAsync(3);
        await PotFixture.TopUpExpectingSuccessAsync(members[0].Client, potId, 100m, members[0].AccountId);

        var ledger = await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 25m,
            PotFixture.Evenly(members[2].MembershipId),
            title: "Tomas's train ticket");

        var positions = PotFixture.PositionsOf(ledger);

        Assert.Equal(100m, positions[members[0].Name]);
        Assert.Equal(0m, positions[members[1].Name]);
        Assert.Equal(-25m, positions[members[2].Name]);
        PotFixture.AssertInvariant(ledger);
    }

    // ── Out of pocket ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Fronting_an_expense_leaves_the_pot_balance_alone_and_cancels_in_the_sum()
    {
        var (potId, members) = await PotOfAsync(2);

        // Deliberately a dry pot: this is the case out-of-pocket exists for.
        var ledger = await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 40m,
            PotFixture.Evenly(members[0].MembershipId, members[1].MembershipId),
            title: "Alp paid at the till",
            paidFromAccountId: members[0].AccountId);

        // No pot cash moved, because the pot had none and none was used.
        Assert.Equal(0m, PotFixture.BalanceOf(ledger));

        var positions = PotFixture.PositionsOf(ledger);
        Assert.Equal(20m, positions[members[0].Name]);   // fronted 40, consumed 20
        Assert.Equal(-20m, positions[members[1].Name]);  // consumed 20, contributed nothing
        PotFixture.AssertInvariant(ledger);

        // The payer's own account really is 40 lighter — that is the whole point
        // of recording it rather than pretending the pot paid.
        Assert.Equal(960m, await ApiClient.GetAccountBalanceAsync(members[0].Client, members[0].AccountId));
        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(members[1].Client, members[1].AccountId));
    }

    [Fact]
    public async Task Nobody_can_say_that_somebody_else_paid_out_of_their_pocket()
    {
        var (potId, members) = await PotOfAsync(2);

        // Mara names Alp's account. It is not hers, and the user-scoped balance
        // adjustment simply does not find it — the same answer somebody would
        // get for an account id that never existed, which is the point.
        var response = await PotFixture.LogExpenseAsync(
            members[1].Client, potId, 10m,
            PotFixture.Evenly(members[1].MembershipId),
            paidFromAccountId: members[0].AccountId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Account not found", await PotFixture.MessageAsync(response));

        // And nothing moved anywhere.
        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(members[0].Client, members[0].AccountId));
        Assert.Empty(PotFixture.EntriesOf(await PotFixture.LedgerAsync(members[0].Client, potId)));
    }

    // ── Acceptance 4 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_pot_expense_restores_the_balance_and_every_position()
    {
        var (potId, members) = await PotOfAsync(3);

        foreach (var member in members)
            await PotFixture.TopUpExpectingSuccessAsync(member.Client, potId, 60m, member.AccountId);

        var before = await PotFixture.LedgerAsync(members[0].Client, potId);
        var balanceBefore = PotFixture.BalanceOf(before);
        var positionsBefore = PotFixture.PositionsOf(before);

        var afterSpending = await PotFixture.LogExpenseExpectingSuccessAsync(
            members[1].Client, potId, 47.53m,
            PotFixture.Evenly(members.Select(m => m.MembershipId).ToArray()),
            title: "The big shop");

        Assert.NotEqual(balanceBefore, PotFixture.BalanceOf(afterSpending));

        var expenseId = Assert.Single(
            PotFixture.EntriesOf(afterSpending), e => e.GetProperty("kind").GetInt32() == 1)
            .GetProperty("id").GetGuid();

        // Flat trust: a member who did not log it removes it.
        var deleted = await PotFixture.DeleteExpenseAsync(members[2].Client, potId, expenseId);
        deleted.EnsureSuccessStatusCode();

        var after = await PotFixture.ReadAsync(deleted);

        Assert.Equal(balanceBefore, PotFixture.BalanceOf(after));
        Assert.Equal(positionsBefore, PotFixture.PositionsOf(after));
        PotFixture.AssertInvariant(after);

        // The shares went with it — not merely stopped counting.
        Assert.DoesNotContain(PotFixture.EntriesOf(after), e => e.GetProperty("kind").GetInt32() == 1);

        // And the log says it happened, because a change nobody can see is a
        // change flat trust cannot survive.
        Assert.Contains(10, await PotFixture.ActivityKindsAsync(members[0].Client, potId));
    }

    [Fact]
    public async Task Deleting_a_fronted_expense_gives_the_payer_their_money_back()
    {
        var (potId, members) = await PotOfAsync(2);

        var logged = await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 33.33m,
            PotFixture.Evenly(members[0].MembershipId, members[1].MembershipId),
            paidFromAccountId: members[0].AccountId);

        Assert.Equal(966.67m, await ApiClient.GetAccountBalanceAsync(members[0].Client, members[0].AccountId));

        var expenseId = Assert.Single(PotFixture.EntriesOf(logged)).GetProperty("id").GetGuid();

        (await PotFixture.DeleteExpenseAsync(members[0].Client, potId, expenseId)).EnsureSuccessStatusCode();

        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(members[0].Client, members[0].AccountId));

        var after = await PotFixture.LedgerAsync(members[0].Client, potId);
        Assert.Equal(0m, PotFixture.BalanceOf(after));
        Assert.All(PotFixture.PositionsOf(after).Values, p => Assert.Equal(0m, p));
    }

    [Fact]
    public async Task Only_the_member_who_fronted_an_expense_can_remove_it()
    {
        var (potId, members) = await PotOfAsync(2);

        var logged = await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 20m,
            PotFixture.Evenly(members[0].MembershipId, members[1].MembershipId),
            paidFromAccountId: members[0].AccountId);

        var expense = Assert.Single(PotFixture.EntriesOf(logged));
        var expenseId = expense.GetProperty("id").GetGuid();

        // Reversing it would have to credit Alp's account, and no user may write
        // into an account they do not own. The genuine cross-user write is W3's.
        var refused = await PotFixture.DeleteExpenseAsync(members[1].Client, potId, expenseId);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            "Only the member who paid for this out of their own pocket can remove it.",
            await PotFixture.MessageAsync(refused));

        // The screen says so before the attempt, too, so the button is not
        // offered and then refused.
        var seenByMara = Assert.Single(PotFixture.EntriesOf(await PotFixture.LedgerAsync(members[1].Client, potId)));
        Assert.False(seenByMara.GetProperty("canDelete").GetBoolean());
        Assert.True(expense.GetProperty("canDelete").GetBoolean());
    }

    // ── Acceptance 6 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_withdrawal_is_capped_at_the_members_positive_position()
    {
        var (potId, members) = await PotOfAsync(2);

        await PotFixture.TopUpExpectingSuccessAsync(members[0].Client, potId, 100m, members[0].AccountId);
        await PotFixture.TopUpExpectingSuccessAsync(members[1].Client, potId, 20m, members[1].AccountId);

        // Mara has 20 in the pot and the pot holds 120. She may not have Alp's.
        var refused = await PotFixture.WithdrawAsync(members[1].Client, potId, 50m, members[1].AccountId);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            "You can take out at most 20.00 EUR — that is your share of what is in the pot.",
            await PotFixture.MessageAsync(refused));

        // Nothing moved: not the pot, not her account, not the feed.
        var ledger = await PotFixture.LedgerAsync(members[1].Client, potId);
        Assert.Equal(120m, PotFixture.BalanceOf(ledger));
        Assert.Equal(980m, await ApiClient.GetAccountBalanceAsync(members[1].Client, members[1].AccountId));
        Assert.DoesNotContain(PotFixture.EntriesOf(ledger), e => e.GetProperty("kind").GetInt32() == 2);

        // Exactly her position is allowed, and lands in an account she owns.
        var after = await PotFixture.WithdrawExpectingSuccessAsync(
            members[1].Client, potId, 20m, members[1].AccountId);

        Assert.Equal(100m, PotFixture.BalanceOf(after));
        Assert.Equal(0m, PotFixture.PositionsOf(after)[members[1].Name]);
        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(members[1].Client, members[1].AccountId));
        PotFixture.AssertInvariant(after);
    }

    [Fact]
    public async Task A_member_who_has_overspent_can_withdraw_nothing()
    {
        var (potId, members) = await PotOfAsync(2);

        await PotFixture.TopUpExpectingSuccessAsync(members[0].Client, potId, 100m, members[0].AccountId);
        await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 30m, PotFixture.Evenly(members[1].MembershipId));

        var refused = await PotFixture.WithdrawAsync(members[1].Client, potId, 1m, members[1].AccountId);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("You have nothing in this pot to take out.", await PotFixture.MessageAsync(refused));
    }

    [Fact]
    public async Task Nobody_can_withdraw_more_cash_than_the_pot_actually_holds()
    {
        var (potId, members) = await PotOfAsync(2);

        // Alp fronts 100 out of pocket for Mara alone, so his position is +100
        // and the pot's cash is still zero. His stake is real; the cash to pay
        // it out with is not here — that is what a settlement is for, in W3.
        await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 100m,
            PotFixture.Evenly(members[1].MembershipId),
            paidFromAccountId: members[0].AccountId);

        var ledger = await PotFixture.LedgerAsync(members[0].Client, potId);
        var mine = ledger.GetProperty("positions").EnumerateArray()
                         .First(p => p.GetProperty("isYou").GetBoolean());

        Assert.Equal(100m, mine.GetProperty("position").GetDecimal());
        Assert.Equal(0m, mine.GetProperty("withdrawable").GetDecimal());

        var refused = await PotFixture.WithdrawAsync(members[0].Client, potId, 50m, members[0].AccountId);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // ── Currency ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Group_amounts_stay_in_the_pots_currency_whatever_the_member_uses()
    {
        var users = await PotFixture.UsersAsync(_factory, 2);

        var potId = await PotFixture.CreatePotIdAsync(users[0].Client, "Trip", currencyCode: "EUR");
        await PotFixture.JoinExpectingSuccessAsync(
            users[1].Client, await PotFixture.InviteCodeAsync(users[0].Client, potId));

        // Mara's account is in a currency worth half a euro. The pot is in EUR
        // and stays in EUR: what she typed is what the group sees, because a
        // group screen that converted would show four members four different
        // numbers for one bill.
        var currencyId = await ApiClient.CreateCurrencyAsync(users[1].Client, "USD", rate: 0.5m);
        var accountId = await ApiClient.CreateAccountAsync(users[1].Client, currencyId, 500m);

        var ledger = await PotFixture.TopUpExpectingSuccessAsync(users[1].Client, potId, 40m, accountId);

        Assert.Equal("EUR", ledger.GetProperty("currencyCode").GetString());
        Assert.Equal(40m, PotFixture.BalanceOf(ledger));

        // Read through the other member's token, the figure is identical — there
        // is one number here, not one per reader.
        Assert.Equal(40m, PotFixture.BalanceOf(await PotFixture.LedgerAsync(users[0].Client, potId)));
    }

    // ── Privacy, carried forward from W1 ───────────────────────────────────────

    [Fact]
    public async Task A_non_member_gets_404_on_every_ledger_route()
    {
        var users = await PotFixture.UsersAsync(_factory, 3);
        var (alp, mara, tomas) = (users[0], users[1], users[2]);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        var alpMembership = await PotFixture.MyMembershipIdAsync(alp.Client, potId);
        var alpAccount = await PotFixture.FundedAccountAsync(alp.Client);
        await PotFixture.TopUpExpectingSuccessAsync(alp.Client, potId, 50m, alpAccount);

        var logged = await PotFixture.LogExpenseExpectingSuccessAsync(
            alp.Client, potId, 10m, PotFixture.Evenly(alpMembership));
        var expenseId = Assert.Single(PotFixture.EntriesOf(logged), e => e.GetProperty("kind").GetInt32() == 1)
                              .GetProperty("id").GetGuid();

        var tomasAccount = await PotFixture.FundedAccountAsync(tomas.Client);

        // 404 and never 403, on the write routes as well as the read: a 403
        // would confirm the pot is there, and the existence of a group you were
        // not invited to is itself private.
        var attempts = new (string Route, Func<Task<HttpResponseMessage>> Send)[]
        {
            ($"GET /ledger", () => tomas.Client.GetAsync($"/api/Pot/{potId}/ledger")),
            ($"POST /topups", () => PotFixture.TopUpAsync(tomas.Client, potId, 10m, tomasAccount)),
            ($"POST /expenses", () => PotFixture.LogExpenseAsync(
                tomas.Client, potId, 10m, PotFixture.Evenly(alpMembership))),
            ($"DELETE /expenses", () => PotFixture.DeleteExpenseAsync(tomas.Client, potId, expenseId)),
            ($"POST /withdrawals", () => PotFixture.WithdrawAsync(tomas.Client, potId, 10m, tomasAccount)),
        };

        foreach (var (route, send) in attempts)
        {
            var response = await send();
            Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                $"{route} answered {(int)response.StatusCode} — a non-member must get 404, never 403.");
        }

        // And none of it moved anything.
        Assert.Equal(40m, PotFixture.BalanceOf(await PotFixture.LedgerAsync(alp.Client, potId)));
        Assert.Equal(10_000m, await ApiClient.GetAccountBalanceAsync(tomas.Client, tomasAccount));
    }

    [Fact]
    public async Task A_ledger_never_names_a_members_account()
    {
        var (potId, members) = await PotOfAsync(2);

        await PotFixture.TopUpExpectingSuccessAsync(members[0].Client, potId, 50m, members[0].AccountId);
        await PotFixture.WithdrawExpectingSuccessAsync(members[0].Client, potId, 10m, members[0].AccountId);
        await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 5m,
            PotFixture.Evenly(members[0].MembershipId),
            paidFromAccountId: members[0].AccountId);

        var body = await (await members[1].Client.GetAsync($"/api/Pot/{potId}/ledger"))
            .Content.ReadAsStringAsync();

        // "Members see pot activity only — never which of your accounts funded a
        // top-up." Three different rows carry an account id in the database and
        // none of them may carry it out.
        Assert.DoesNotContain(members[0].AccountId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(members[0].User.UserId, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accountId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("categoryId", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── Refusals worth a sentence ──────────────────────────────────────────────

    [Fact]
    public async Task An_expense_cannot_be_attributed_to_somebody_in_another_pot()
    {
        var (potId, members) = await PotOfAsync(2);
        var (otherPotId, others) = await PotOfAsync(2);

        var response = await PotFixture.LogExpenseAsync(
            members[0].Client, potId, 10m,
            PotFixture.Evenly(members[0].MembershipId, others[0].MembershipId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("One of the people you picked is not in this pot.", await PotFixture.MessageAsync(response));
        Assert.Empty(PotFixture.EntriesOf(await PotFixture.LedgerAsync(members[0].Client, potId)));

        // And the other pot is untouched, which is the half of this that would
        // be a real leak rather than a mistake.
        Assert.Equal(0m, PotFixture.BalanceOf(await PotFixture.LedgerAsync(others[0].Client, otherPotId)));
    }

    [Fact]
    public async Task Shares_that_add_up_to_more_than_the_expense_are_refused()
    {
        var (potId, members) = await PotOfAsync(2);

        var response = await PotFixture.LogExpenseAsync(
            members[0].Client, potId, 10m,
            new[]
            {
                (members[0].MembershipId, (decimal?)8m),
                (members[1].MembershipId, (decimal?)7m),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Those shares add up to more than the expense.", await PotFixture.MessageAsync(response));
    }

    [Fact]
    public async Task A_top_up_from_an_account_the_member_does_not_own_moves_nothing()
    {
        var (potId, members) = await PotOfAsync(2);

        var response = await PotFixture.TopUpAsync(
            members[1].Client, potId, 25m, members[0].AccountId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(members[0].Client, members[0].AccountId));
        Assert.Equal(0m, PotFixture.BalanceOf(await PotFixture.LedgerAsync(members[0].Client, potId)));
    }

    // ── The feed, and the log ──────────────────────────────────────────────────

    [Fact]
    public async Task Every_money_operation_writes_one_line_in_the_activity_log()
    {
        var (potId, members) = await PotOfAsync(2);

        await PotFixture.TopUpExpectingSuccessAsync(members[0].Client, potId, 100m, members[0].AccountId);
        var logged = await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 10m, PotFixture.Evenly(members[0].MembershipId));
        await PotFixture.WithdrawExpectingSuccessAsync(members[0].Client, potId, 5m, members[0].AccountId);

        var expenseId = Assert.Single(PotFixture.EntriesOf(logged), e => e.GetProperty("kind").GetInt32() == 1)
                              .GetProperty("id").GetGuid();
        (await PotFixture.DeleteExpenseAsync(members[0].Client, potId, expenseId)).EnsureSuccessStatusCode();

        // ToppedUp, ExpenseLogged, Withdrew, ExpenseDeleted — flat trust means
        // the log is the accountability, so money moving without a line in it
        // would be money moving nobody can see.
        var kinds = await PotFixture.ActivityKindsAsync(members[1].Client, potId);
        foreach (var kind in new[] { 8, 9, 10, 11 })
            Assert.Contains(kind, kinds);
    }

    [Fact]
    public async Task The_feed_carries_all_three_kinds_newest_first()
    {
        var (potId, members) = await PotOfAsync(2);

        await PotFixture.TopUpExpectingSuccessAsync(
            members[0].Client, potId, 100m, members[0].AccountId, date: "2026-08-01");
        await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 10m, PotFixture.Evenly(members[0].MembershipId), date: "2026-08-03");
        await PotFixture.WithdrawExpectingSuccessAsync(members[0].Client, potId, 5m, members[0].AccountId);

        var entries = PotFixture.EntriesOf(await PotFixture.LedgerAsync(members[1].Client, potId));

        Assert.Equal(3, entries.Count);
        Assert.Equal(
            entries.Select(e => e.GetProperty("date").GetString()).OrderByDescending(d => d),
            entries.Select(e => e.GetProperty("date").GetString()));

        // Each line names who did it, because that is the only identity a pot
        // has for a person.
        Assert.All(entries, e => Assert.Equal(members[0].Name, e.GetProperty("actorDisplayName").GetString()));
    }

    [Fact]
    public async Task The_pot_list_carries_the_balance_and_your_own_position()
    {
        var (potId, members) = await PotOfAsync(2);

        await PotFixture.TopUpExpectingSuccessAsync(members[0].Client, potId, 80m, members[0].AccountId);
        await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 30m,
            PotFixture.Evenly(members[0].MembershipId, members[1].MembershipId));

        var alpsView = Assert.Single(await PotFixture.MyPotsAsync(members[0].Client));
        var marasView = Assert.Single(await PotFixture.MyPotsAsync(members[1].Client));

        // One balance, two positions — the pot's money is a fact about the pot,
        // and where you stand against it is a fact about you.
        Assert.Equal(50m, alpsView.GetProperty("balance").GetDecimal());
        Assert.Equal(50m, marasView.GetProperty("balance").GetDecimal());

        Assert.Equal(65m, alpsView.GetProperty("yourPosition").GetDecimal());
        Assert.Equal(-15m, marasView.GetProperty("yourPosition").GetDecimal());

        Assert.False(alpsView.GetProperty("isLow").GetBoolean());
    }

    [Fact]
    public async Task A_member_who_left_keeps_their_place_in_the_arithmetic()
    {
        var (potId, members) = await PotOfAsync(2);

        await PotFixture.TopUpExpectingSuccessAsync(members[0].Client, potId, 60m, members[0].AccountId);
        await PotFixture.LogExpenseExpectingSuccessAsync(
            members[0].Client, potId, 20m,
            PotFixture.Evenly(members[0].MembershipId, members[1].MembershipId));

        (await PotFixture.LeaveAsync(members[1].Client, potId)).EnsureSuccessStatusCode();

        var ledger = await PotFixture.LedgerAsync(members[0].Client, potId);

        // Leaving with a non-zero position is refused in W3, deliberately not
        // here. What matters now is that a departure cannot make the pot's
        // arithmetic stop adding up — Mara consumed 10 and that 10 is still part
        // of the sum, under her name.
        Assert.Equal(-10m, PotFixture.PositionsOf(ledger)[members[1].Name]);
        PotFixture.AssertInvariant(ledger);

        var hers = Assert.Single(
            ledger.GetProperty("positions").EnumerateArray(),
            p => p.GetProperty("displayName").GetString() == members[1].Name);

        Assert.False(hers.GetProperty("isActive").GetBoolean());
    }
}
