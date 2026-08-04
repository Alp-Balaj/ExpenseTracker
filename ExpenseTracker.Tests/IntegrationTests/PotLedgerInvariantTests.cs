using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace ExpenseTracker.Tests.IntegrationTests;

/// <summary>
/// Cycle U W2's centrepiece: <c>Σ Bᵢ ≡ pot cash balance</c>, after every single
/// operation, over sequences nobody wrote by hand.
///
/// <para>
/// The example-based tests next door check the cases somebody thought of. This
/// one is here for the cases nobody did. It drives long random sequences of the
/// four operations — top up, spend from the pot, front an expense out of
/// pocket, withdraw — against four real members over the real HTTP API, and
/// re-asserts the identity after every step. A drift of one cent, on step 47 of
/// a sequence with a particular interleaving, is exactly the failure that would
/// otherwise reach production and be discovered as "the numbers are slightly
/// wrong" six weeks later.
/// </para>
///
/// <para>
/// <b>Seeded, not arbitrary.</b> The sequences come from a fixed set of seeds,
/// so a failure is reproducible from the message alone: the same seed replays
/// the same sequence. A test that fails once and never again is not a test.
/// </para>
///
/// <para>
/// Amounts are drawn to make rounding hurt on purpose — prices ending in odd
/// cents, split across two or three of four people, so the largest-remainder
/// allocator is exercised rather than skirted by amounts that happen to divide.
/// </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PotLedgerInvariantTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public PotLedgerInvariantTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    /// <summary>One member, set up to act: their client, their membership, their account.</summary>
    private sealed record Actor(HttpClient Client, string Name, Guid MembershipId, Guid AccountId);

    /// <summary>
    /// Four members in one pot, each with a funded EUR account — W0's cohort
    /// size, because the tie-break was decided against a concrete member count
    /// and because four is where an even split stops being trivial.
    /// </summary>
    private async Task<(Guid PotId, IReadOnlyList<Actor> Actors)> FourMembersAsync()
    {
        var users = await PotFixture.UsersAsync(_factory, 4);

        var potId = await PotFixture.CreatePotIdAsync(users[0].Client, "Flat");
        var code = await PotFixture.InviteCodeAsync(users[0].Client, potId);

        // Sequentially, because join order is the rounding tie-break and a
        // parallel join would make it a coin flip.
        foreach (var user in users.Skip(1))
            await PotFixture.JoinExpectingSuccessAsync(user.Client, code);

        var actors = new List<Actor>(users.Count);
        foreach (var user in users)
            actors.Add(new Actor(
                user.Client,
                user.DisplayName,
                await PotFixture.MyMembershipIdAsync(user.Client, potId),
                await PotFixture.FundedAccountAsync(user.Client, balance: 100_000m)));

        return (potId, actors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(20260804)]
    public async Task The_sum_of_positions_is_the_pot_balance_after_every_operation(int seed)
    {
        const int Steps = 40;

        var random = new Random(seed);
        var (potId, actors) = await FourMembersAsync();

        // The reader whose eyes the invariant is checked through. It has to hold
        // for whoever is looking — the positions are the same rows however they
        // are read — so one reader is enough and any of them will do.
        var reader = actors[0].Client;

        var log = new List<string>(Steps);

        for (var step = 0; step < Steps; step++)
        {
            var actor = actors[random.Next(actors.Count)];
            var description = await ActAsync(random, potId, actor, actors, reader);
            log.Add($"{step:D2} {actor.Name}: {description}");

            var ledger = await PotFixture.LedgerAsync(reader, potId);

            // The identity, and then the arithmetic behind each term of it — a
            // position that summed correctly out of four wrong numbers would
            // still be wrong, and would still be the thing somebody reads.
            PotFixture.AssertInvariant(ledger, $"seed {seed}, after step {step} ({log[^1]})");
            AssertEachPositionIsItsOwnTerms(ledger, seed, step);
        }

        _output.WriteLine($"seed {seed}:\n  {string.Join("\n  ", log)}");
    }

    /// <summary>
    /// Performs one random operation and says what it was, so a failure names
    /// the step that broke rather than only the step it was noticed at.
    /// </summary>
    private static async Task<string> ActAsync(
        Random random, Guid potId, Actor actor, IReadOnlyList<Actor> actors, HttpClient reader)
    {
        // Weighted so the pot usually has money in it: an unweighted draw spends
        // a dry pot most of the time, and "the refusal path works" is a
        // different test from this one.
        var roll = random.Next(100);

        if (roll < 35)
        {
            var amount = Cents(random, 500, 20_000);
            await PotFixture.TopUpExpectingSuccessAsync(actor.Client, potId, amount, actor.AccountId);
            return $"topped up {amount}";
        }

        if (roll < 90)
        {
            // Two or three of the four, so an even split rarely divides cleanly
            // and the leftover cent has to land somewhere deterministic.
            var participants = actors
                .OrderBy(_ => random.Next())
                .Take(random.Next(2, 4))
                .Select(a => a.MembershipId)
                .ToArray();

            var amount = Cents(random, 101, 9_999);
            var outOfPocket = roll >= 75;

            // Some of the time, one participant is pinned to an exact amount and
            // the rest divide the remainder — the "custom amounts" affordance,
            // which is where an allocator is most likely to lose a cent.
            var shares = random.Next(3) == 0 && participants.Length > 1
                ? Pinned(random, participants, amount)
                : participants.Select(id => (id, (decimal?)null));

            var response = await PotFixture.LogExpenseAsync(
                actor.Client, potId, amount, shares,
                title: $"Expense {amount}",
                paidFromAccountId: outOfPocket ? actor.AccountId : null);

            // A pot-paid expense on a dry pot is allowed — the pot simply goes
            // negative in the sense that somebody has overspent, which shows up
            // as negative positions and is a real state, not an error. So this
            // must succeed; anything else is the failure, not a skip.
            Assert.True(response.IsSuccessStatusCode,
                $"expense failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");

            return $"spent {amount} across {participants.Length}{(outOfPocket ? " out of pocket" : "")}";
        }

        // Withdrawals are capped at what is actually the member's, so this asks
        // the ledger what that is rather than guessing — and skips when it is
        // nothing, which is a legitimate state and not a missed operation.
        var mine = (await PotFixture.LedgerAsync(reader, potId))
            .GetProperty("positions")
            .EnumerateArray()
            .First(p => p.GetProperty("membershipId").GetGuid() == actor.MembershipId);

        var withdrawable = mine.GetProperty("withdrawable").GetDecimal();
        if (withdrawable < 0.01m) return "nothing to withdraw";

        var taken = decimal.Round(withdrawable * (decimal)random.NextDouble(), 2);
        if (taken < 0.01m) taken = 0.01m;

        await PotFixture.WithdrawExpectingSuccessAsync(actor.Client, potId, taken, actor.AccountId);
        return $"withdrew {taken} of {withdrawable}";
    }

    /// <summary>
    /// Pins one participant to a slice of the total and leaves the rest open, so
    /// the allocator has to divide a remainder rather than the whole amount.
    /// </summary>
    private static IEnumerable<(Guid, decimal?)> Pinned(Random random, Guid[] participants, decimal amount)
    {
        // Somewhere between a tenth and half the bill, on a cent boundary that
        // rarely divides by the number of people left.
        var pinnedAmount = decimal.Round(amount * (decimal)(0.1 + random.NextDouble() * 0.4), 2);
        if (pinnedAmount < 0.01m) pinnedAmount = 0.01m;

        return participants.Select((id, i) => (id, i == 0 ? pinnedAmount : (decimal?)null));
    }

    /// <summary>An amount in whole cents, deliberately not a round number of euros.</summary>
    private static decimal Cents(Random random, int minCents, int maxCents) =>
        random.Next(minCents, maxCents) / 100m;

    /// <summary>
    /// Every member's position is exactly its own four terms. The sum can be
    /// right while a term is wrong — two errors that cancel across members would
    /// pass the identity and still tell one person the wrong thing about their
    /// own money.
    /// </summary>
    private static void AssertEachPositionIsItsOwnTerms(JsonElement ledger, int seed, int step)
    {
        foreach (var position in ledger.GetProperty("positions").EnumerateArray())
        {
            var expected =
                position.GetProperty("toppedUp").GetDecimal()
                + position.GetProperty("outOfPocket").GetDecimal()
                - position.GetProperty("shares").GetDecimal()
                - position.GetProperty("withdrawn").GetDecimal();

            Assert.True(expected == position.GetProperty("position").GetDecimal(),
                $"seed {seed}, step {step}: {position.GetProperty("displayName").GetString()}'s "
                + $"position is not its own terms");
        }
    }

    /// <summary>
    /// The other half of the identity, asserted directly rather than through the
    /// endpoint that computes both: every expense's shares add up to the expense
    /// itself, to the cent, however the split was entered. This is the property
    /// the pot balance depends on and the one the allocator exists to guarantee.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public async Task Every_expense_shares_add_up_to_the_expense(int seed)
    {
        var random = new Random(seed);
        var (potId, actors) = await FourMembersAsync();

        for (var i = 0; i < 25; i++)
        {
            var amount = Cents(random, 1, 50_000);
            var participants = actors
                .OrderBy(_ => random.Next())
                .Take(random.Next(1, 5))
                .Select(a => a.MembershipId)
                .ToArray();

            await PotFixture.LogExpenseExpectingSuccessAsync(
                actors[0].Client, potId, amount,
                random.Next(2) == 0 && participants.Length > 1
                    ? Pinned(random, participants, amount)
                    : participants.Select(id => (id, (decimal?)null)),
                title: $"Expense {i}");
        }

        var ledger = await PotFixture.LedgerAsync(actors[0].Client, potId);

        foreach (var entry in PotFixture.EntriesOf(ledger).Where(e => e.GetProperty("kind").GetInt32() == 1))
        {
            var amount = entry.GetProperty("amount").GetDecimal();
            var shares = entry.GetProperty("shares").EnumerateArray().Sum(s => s.GetProperty("amount").GetDecimal());

            Assert.True(amount == shares,
                $"seed {seed}: '{entry.GetProperty("title").GetString()}' is {amount} but its shares are {shares}");
        }
    }
}
