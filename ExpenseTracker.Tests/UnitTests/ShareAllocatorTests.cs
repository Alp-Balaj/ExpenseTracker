using ExpenseTracker.Application.Services;
using Xunit;

namespace ExpenseTracker.Tests.UnitTests;

/// <summary>
/// The arithmetic underneath <c>Σ Bᵢ ≡ pot cash balance</c>, tested without a
/// database in the way.
///
/// <para>
/// The integration suite proves the identity holds across the whole feature;
/// this proves the one function it rests on, at the boundaries a random
/// sequence would take a long time to stumble into. Both are worth having: a
/// failure here says which line is wrong, a failure there says the feature is.
/// </para>
/// </summary>
public sealed class ShareAllocatorTests
{
    [Fact]
    public void Ten_across_three_is_three_thirty_four_and_two_thirty_threes()
    {
        // The charter's own worked example, and the reason largest remainder is
        // used at all: 3.33 three times loses a cent, and a hundred expenses
        // later the positions stop describing the pot's money.
        Assert.Equal(new[] { 334L, 333L, 333L }, ShareAllocator.Even(1000, 3));
    }

    [Fact]
    public void Ten_oh_one_across_four_gives_the_extra_cent_to_the_earliest_joined()
    {
        // W0 wrote this one down against the cohort's fixed order: Alp joined
        // first, so Alp absorbs it. The allocator does not know about people —
        // the caller passes them already sorted by (CreatedAt, Id), and
        // "earliest slot" is what that ordering means.
        Assert.Equal(new[] { 251L, 250L, 250L, 250L }, ShareAllocator.Even(1001, 4));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 4)]
    [InlineData(999_999, 7)]
    [InlineData(100, 100)]
    [InlineData(99, 100)]
    public void An_even_split_always_adds_back_up(long totalCents, int count)
    {
        var shares = ShareAllocator.Even(totalCents, count);

        Assert.Equal(count, shares.Length);
        Assert.Equal(totalCents, shares.Sum());

        // And nobody is more than a cent better off than anybody else, which is
        // what makes it a split rather than merely a partition.
        Assert.True(shares.Max() - shares.Min() <= 1);
    }

    [Fact]
    public void Every_slot_open_divides_the_whole_amount()
    {
        var (shares, error) = ShareAllocator.Allocate(1000, [null, null, null]);

        Assert.Null(error);
        Assert.Equal(new[] { 334L, 333L, 333L }, shares);
    }

    [Fact]
    public void One_slot_open_takes_everything_left()
    {
        // "Custom amounts": two people typed a figure, the third gets the rest.
        var (shares, error) = ShareAllocator.Allocate(1000, [250L, 100L, null]);

        Assert.Null(error);
        Assert.Equal(new[] { 250L, 100L, 650L }, shares);
    }

    [Fact]
    public void The_remainder_divides_across_the_open_slots_by_largest_remainder()
    {
        // 10.00 with 0.01 pinned leaves 9.99 across three — which divides
        // exactly, so the interesting case is the one below it.
        var (shares, error) = ShareAllocator.Allocate(1000, [1L, null, null, null]);

        Assert.Null(error);
        Assert.Equal(new[] { 1L, 333L, 333L, 333L }, shares);
        Assert.Equal(1000L, shares!.Sum());
    }

    [Fact]
    public void A_pinned_amount_that_leaves_an_indivisible_remainder_still_adds_up()
    {
        var (shares, error) = ShareAllocator.Allocate(1000, [1L, null, null]);

        Assert.Null(error);
        Assert.Equal(new[] { 1L, 500L, 499L }, shares);
        Assert.Equal(1000L, shares!.Sum());
    }

    [Fact]
    public void All_slots_fixed_and_adding_up_is_taken_as_typed()
    {
        var (shares, error) = ShareAllocator.Allocate(1000, [400L, 350L, 250L]);

        Assert.Null(error);
        Assert.Equal(new[] { 400L, 350L, 250L }, shares);
    }

    [Fact]
    public void All_slots_fixed_and_not_adding_up_is_refused_rather_than_stretched()
    {
        // There is nowhere for the difference to go, and quietly widening the
        // last slot to absorb it would be the app inventing a number nobody
        // typed. The sentence is what the person entering it reads.
        var (shares, error) = ShareAllocator.Allocate(1000, [400L, 350L, 200L]);

        Assert.Null(shares);
        Assert.Equal("Those shares have to add up to the expense.", error);
    }

    [Fact]
    public void Fixed_amounts_over_the_total_are_refused()
    {
        var (shares, error) = ShareAllocator.Allocate(1000, [800L, 700L, null]);

        Assert.Null(shares);
        Assert.Equal("Those shares add up to more than the expense.", error);
    }

    [Fact]
    public void A_negative_share_is_refused()
    {
        var (shares, error) = ShareAllocator.Allocate(1000, [-100L, null]);

        Assert.Null(shares);
        Assert.Equal("A share cannot be negative.", error);
    }

    [Fact]
    public void An_expense_for_nobody_is_refused()
    {
        var (shares, error) = ShareAllocator.Allocate(1000, []);

        Assert.Null(shares);
        Assert.Equal("Say who this expense was for.", error);
    }

    [Fact]
    public void A_fixed_slot_taking_the_whole_amount_leaves_the_open_ones_at_zero()
    {
        // Legitimate: "this was for me, but log it against the group" with the
        // others explicitly on the expense at nothing. Zero shares are real
        // rows and are allowed to be.
        var (shares, error) = ShareAllocator.Allocate(1000, [1000L, null, null]);

        Assert.Null(error);
        Assert.Equal(new[] { 1000L, 0L, 0L }, shares);
    }

    [Theory]
    [InlineData(10.00, 1000)]
    [InlineData(10.005, 1001)]
    [InlineData(0.01, 1)]
    [InlineData(1_000_000.00, 100_000_000)]
    public void Cents_round_half_away_from_zero_and_convert_back_exactly(decimal amount, long cents)
    {
        // Money is stored at precision (18,2), so a third decimal place is going
        // to be rounded by the database whatever happens here. Rounding at the
        // door means the allocator and the column agree about what was stored.
        Assert.Equal(cents, ShareAllocator.ToCents(amount));
        Assert.Equal(decimal.Round(amount, 2, MidpointRounding.AwayFromZero), ShareAllocator.FromCents(cents));
    }
}
