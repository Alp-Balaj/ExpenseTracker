namespace ExpenseTracker.Application.Services;

/// <summary>
/// Splits an amount across people so that the parts add up to the whole, every
/// time, with no rounding drift.
///
/// <para>
/// This is the load-bearing arithmetic of Cycle U. <c>Σ Bᵢ ≡ pot cash
/// balance</c> only holds if <c>Σ shares ≡ amount</c> holds for every single
/// expense, and the naive split — divide, round each part — does not: €10.00
/// across three people rounds to 3.33 three times and loses a cent, and a
/// hundred expenses later the pot's positions no longer describe the pot's
/// money. Fairness across many expenses is worth pennies; drift in the sum is
/// worth the whole design.
/// </para>
///
/// <para>
/// So: <b>largest remainder, in integer cents</b>. Everything here is
/// <c>long</c> cents rather than <c>decimal</c> euros, because the invariant is
/// a statement about integers and expressing it in a type that can hold
/// 3.3333333 invites exactly the bug it exists to prevent. Money is stored at
/// <c>HasPrecision(18,2)</c>, so cents are lossless in both directions.
/// </para>
///
/// <para>
/// The tie-break is fixed by the execution plan: <c>PotMembership.CreatedAt</c>
/// then membership id. Under an even split every fractional part is identical,
/// so the tie-break is not an edge case that occasionally matters — it decides
/// the whole allocation of the leftover cents. Ordering on join time rather than
/// on anything about the expense is what makes it stable: the earliest-joined
/// member absorbs the extra cent slightly more often than anybody else, and
/// nobody's slice moves because somebody left the pot afterwards.
/// </para>
/// </summary>
public static class ShareAllocator
{
    /// <summary>
    /// A money amount as whole cents. Rounds to the cent first — the column
    /// behind every amount is <c>HasPrecision(18,2)</c>, so a client that sends
    /// 3.14159 is going to be rounded by Postgres anyway, and rounding here
    /// means the allocator and the database agree about what was stored rather
    /// than differing in the third decimal place.
    /// </summary>
    public static long ToCents(decimal amount) =>
        (long)decimal.Round(decimal.Round(amount, 2, MidpointRounding.AwayFromZero) * 100m, 0);

    /// <summary>
    /// Cents as a money amount. <c>decimal</c> division by 100 is exact for any
    /// long, so this is the true inverse of <see cref="ToCents"/>.
    /// </summary>
    public static decimal FromCents(long cents) => cents / 100m;

    /// <summary>
    /// Splits <paramref name="totalCents"/> evenly across <paramref name="count"/>
    /// slots, giving the leftover cents to the earliest slots.
    ///
    /// <para>
    /// This is the largest-remainder method with equal weights. Each slot's
    /// exact entitlement is <c>totalCents / count</c>; every slot takes the
    /// floor of that, which leaves <c>totalCents % count</c> cents undealt. Each
    /// slot's fractional remainder is identical, so "largest remainder first"
    /// resolves entirely to the caller's order — which is why the caller must
    /// pass memberships already sorted by (CreatedAt, Id).
    /// </para>
    ///
    /// <para>
    /// Negative totals are not a thing a pot expense can have and are refused by
    /// the write path long before here; the floor-and-distribute below is
    /// written for non-negative input and says so.
    /// </para>
    /// </summary>
    /// <returns>
    /// <paramref name="count"/> amounts summing to exactly
    /// <paramref name="totalCents"/>.
    /// </returns>
    public static long[] Even(long totalCents, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCents);

        var each = Math.DivRem(totalCents, count, out var leftover);

        var shares = new long[count];
        for (var i = 0; i < count; i++)
            shares[i] = each + (i < leftover ? 1 : 0);

        return shares;
    }

    /// <summary>
    /// The general case the four entry affordances all reduce to: some slots
    /// carry an amount the user typed, the rest divide what is left.
    ///
    /// <list type="bullet">
    /// <item><b>Everyone</b> — every slot null, so the whole amount divides
    /// evenly across the whole membership.</item>
    /// <item><b>A subset</b> — the same, over fewer slots.</item>
    /// <item><b>Custom amounts</b> — some slots fixed, the remainder divided
    /// across the others.</item>
    /// <item><b>One person</b> — one slot, null, taking all of it.</item>
    /// </list>
    ///
    /// <para>
    /// When every slot is fixed the amounts must already add up: there is
    /// nowhere for a discrepancy to go, and quietly stretching the last slot to
    /// close it would be the app inventing a number the user did not type.
    /// </para>
    /// </summary>
    /// <param name="totalCents">The expense, in cents. Non-negative.</param>
    /// <param name="fixedCents">
    /// One entry per participant, in tie-break order: the cents they were
    /// explicitly assigned, or null to divide the remainder.
    /// </param>
    /// <returns>
    /// The allocation, or an error sentence when the fixed amounts cannot be
    /// reconciled with the total. The sentence is shown to the person typing.
    /// </returns>
    public static (long[]? Shares, string? Error) Allocate(
        long totalCents, IReadOnlyList<long?> fixedCents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCents);

        if (fixedCents.Count == 0)
            return (null, "Say who this expense was for.");

        if (fixedCents.Any(c => c is < 0))
            return (null, "A share cannot be negative.");

        var fixedTotal = fixedCents.Sum(c => c ?? 0L);
        var openCount = fixedCents.Count(c => c is null);

        if (fixedTotal > totalCents)
            return (null, "Those shares add up to more than the expense.");

        if (openCount == 0)
        {
            // Nothing left to absorb a difference, so there must not be one.
            if (fixedTotal != totalCents)
                return (null, "Those shares have to add up to the expense.");

            return (fixedCents.Select(c => c!.Value).ToArray(), null);
        }

        var even = Even(totalCents - fixedTotal, openCount);

        var shares = new long[fixedCents.Count];
        var next = 0;
        for (var i = 0; i < fixedCents.Count; i++)
            shares[i] = fixedCents[i] ?? even[next++];

        return (shares, null);
    }
}
