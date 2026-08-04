using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.Pots
{
    /// <summary>
    /// Something the group spent money on, and who it was for.
    /// <para>
    /// Two independent axes, kept apart because collapsing them is what makes
    /// every confused pot design confused:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Funding</b> — who paid. Either the pot itself
    /// (<see cref="PaidByMembershipId"/> null), or one member out of their own
    /// pocket because the pot was dry.</item>
    /// <item><b>Consumption</b> — who it was <i>for</i>. That is
    /// <see cref="Shares"/>, and it is the only thing attribution adds.</item>
    /// </list>
    /// <para>
    /// An out-of-pocket payment books as a contribution and a consumption in one
    /// gesture, which is why it cancels in <c>Σ Bᵢ</c>: the payer's own
    /// <c>outOfPocket</c> term rises by the full amount, every member's
    /// <c>shares</c> term rises by their slice, and the two sum to zero across
    /// the group. The pot's cash balance does not move, because no pot cash was
    /// spent.
    /// </para>
    /// </summary>
    public class PotExpense : PotEntity, IPotScoped
    {
        public Guid PotId { get; set; }
        public Pot Pot { get; set; } = null!;

        public required string Title { get; set; }
        public string? Description { get; set; }

        /// <summary>
        /// The gross amount, in the pot's currency. <c>Σ Shares.Amount ≡
        /// Amount</c> exactly, in integer cents, enforced on the write path by
        /// the largest-remainder allocator.
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// The day the money was spent. Shares hit their members' personal
        /// budgets on <i>this</i> date, not on the date the expense was typed —
        /// so a bill logged late still lands in the month it belongs to, exactly
        /// as an ordinary expense does.
        /// </summary>
        public DateOnly Date { get; set; }

        /// <summary>Who logged it. Flat trust: any member may log anything, for anyone.</summary>
        public Guid CreatedByMembershipId { get; set; }
        public PotMembership CreatedByMembership { get; set; } = null!;

        /// <summary>
        /// Null when the pot paid — the ordinary case, and the only one that
        /// lowers the pot's cash balance. Set when a member fronted it
        /// personally.
        /// <para>
        /// Attribution is free (anyone may say an expense was for anyone), but
        /// funding is not: only the person whose account the money left may
        /// record that it left. So this is always the member who logged the
        /// expense when it is set at all — nobody declares that somebody else
        /// paid, because nobody may write into somebody else's account.
        /// </para>
        /// </summary>
        public Guid? PaidByMembershipId { get; set; }
        public PotMembership? PaidByMembership { get; set; }

        /// <summary>
        /// The payer's own account, when this was fronted out of pocket. A bare
        /// Guid with no foreign key, for the same two reasons as
        /// <see cref="PotTopUp.SourceAccountId"/>: it is private to the payer,
        /// and a pot's arithmetic must not hang off a row that cascades away
        /// with one member's account.
        /// </summary>
        public Guid? PaidFromAccountId { get; set; }

        /// <summary>
        /// One row per person this was for. Deleting the expense deletes all of
        /// them in the same transaction — anything less would leave consumption
        /// behind with nothing that caused it, and <c>Σ Bᵢ</c> would drift.
        /// </summary>
        public ICollection<PotExpenseShare> Shares { get; set; } = new List<PotExpenseShare>();

        /// <summary>True when a member fronted this rather than the pot paying for it.</summary>
        public bool IsOutOfPocket => PaidByMembershipId is not null;
    }
}
