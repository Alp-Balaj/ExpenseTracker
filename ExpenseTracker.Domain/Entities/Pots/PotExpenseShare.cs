using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.Pots
{
    /// <summary>
    /// One person's slice of one pot expense — the <c>shares</c> term of
    /// <c>Bᵢ</c>, and the row that carries a pot expense into a member's own
    /// budget.
    /// <para>
    /// There is exactly one shape for all four ways of entering an expense.
    /// "Everyone", "a subset", "custom amounts" and "just one person" are input
    /// affordances over these rows and nothing more: each produces N of them,
    /// summing to the expense to the cent. Storing the affordance instead of the
    /// result would mean re-deriving the split every time the membership list
    /// changed, and a member joining next month would silently rewrite last
    /// month's arithmetic.
    /// </para>
    /// </summary>
    public class PotExpenseShare : PotEntity, IPotScoped
    {
        public Guid PotId { get; set; }
        public Pot Pot { get; set; } = null!;

        public Guid PotExpenseId { get; set; }
        public PotExpense PotExpense { get; set; } = null!;

        /// <summary>
        /// Whose consumption this is. A membership, not a user — a share
        /// attributed to somebody who later deletes their account still has to
        /// count, or the sum the whole design rests on stops adding up.
        /// </summary>
        public Guid MembershipId { get; set; }
        public PotMembership Membership { get; set; } = null!;

        /// <summary>In the pot's currency, like everything else inside a pot.</summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// The member's reserved "Shared" category at the moment the share was
        /// written — what makes "undeletable while shares reference it" a real
        /// reference rather than an inference from two other facts.
        /// <para>
        /// A bare Guid with no foreign key, for the same reason the account ids
        /// on this wave's other rows are: <c>Category</c> is a
        /// <c>BaseEntity</c> and cascades from the Identity user, so a real FK
        /// would either take this row with it or make the member undeletable
        /// forever. The delete guard reads this column directly instead.
        /// </para>
        /// <para>
        /// It is per-share rather than derived because the charter keeps the
        /// upgrade path open: per-member category mapping, if it ever arrives,
        /// is then additive and not a rewrite of history.
        /// </para>
        /// </summary>
        public Guid CategoryId { get; set; }
    }
}
