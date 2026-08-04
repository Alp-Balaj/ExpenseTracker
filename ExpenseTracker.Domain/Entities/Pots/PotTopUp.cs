using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.Pots
{
    /// <summary>
    /// Money a member put into the pot from one of their own accounts.
    /// <para>
    /// The <c>topups</c> term of <c>Bᵢ = topups + outOfPocket − shares −
    /// withdrawals</c>, and the only term that raises the pot's cash balance.
    /// </para>
    /// <para>
    /// A top-up is a <b>transfer, not an expense</b>. It debits the member's own
    /// account because the money really left it, and it touches no budget and no
    /// category total, because the money is not consumed yet — it is sitting in
    /// the pot with their name still on it. Consumption happens later, as a
    /// share of a pot expense. This is Stream R's gross-versus-net split read
    /// one level up: the account balance is cash truth, the share is analysis
    /// truth.
    /// </para>
    /// </summary>
    public class PotTopUp : PotEntity, IPotScoped
    {
        public Guid PotId { get; set; }
        public Pot Pot { get; set; } = null!;

        /// <summary>
        /// Who contributed, as a membership rather than a user id — so the
        /// contribution is still attributed after the account behind it is gone,
        /// and so <c>Σ Bᵢ</c> keeps summing over the same set of rows it always
        /// did.
        /// </summary>
        public Guid MembershipId { get; set; }
        public PotMembership Membership { get; set; } = null!;

        /// <summary>In the pot's currency. Group amounts are never converted.</summary>
        public decimal Amount { get; set; }

        public DateOnly Date { get; set; }

        public string? Note { get; set; }

        /// <summary>
        /// The member's own account the money came out of — <b>a bare Guid with
        /// no foreign key and no navigation</b>, and that is deliberate on two
        /// counts.
        /// <para>
        /// Privacy first: the charter's rule is that members see pot activity
        /// only, "never which of your accounts funded a top-up". This id is used
        /// once, on the write, to debit the balance through the user-scoped
        /// account service — which is also what proves the account belongs to
        /// the person spending from it — and is never put in a DTO.
        /// </para>
        /// <para>
        /// Lifetime second: <c>Account</c> is a <c>BaseEntity</c>, so
        /// <c>ConfigureUserOwnership</c> cascades it from the Identity user. A
        /// real FK here would mean a member deleting their account either taking
        /// the group's ledger rows with it (Cascade) or being unable to delete
        /// at all (Restrict) — and unlike a membership, an account has no
        /// anonymize step to make Restrict survivable. A pot's arithmetic must
        /// not depend on a row only one member can see.
        /// </para>
        /// </summary>
        public Guid SourceAccountId { get; set; }
    }
}
