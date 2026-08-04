using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.Pots
{
    /// <summary>
    /// A member taking their own unspent stake back out of the pot.
    /// <para>
    /// Capped at their positive position, which is the whole rule: what you may
    /// withdraw is what is still yours. Nobody needs the group's approval and
    /// nobody can drain somebody else's stake — the cap is not a courtesy, it is
    /// what keeps <c>Σ Bᵢ ≡ pot cash balance</c> from going somewhere it cannot
    /// come back from.
    /// </para>
    /// <para>
    /// A withdrawal is <b>not</b> a settlement. Settling is paying another
    /// member back for consumption, personal account to personal account, and it
    /// arrives in W3. This is money leaving the pot to the person it already
    /// belonged to. The charter separated the two the day it superseded "the pot
    /// is the settlement", and they stay separate here.
    /// </para>
    /// </summary>
    public class PotWithdrawal : PotEntity, IPotScoped
    {
        public Guid PotId { get; set; }
        public Pot Pot { get; set; } = null!;

        public Guid MembershipId { get; set; }
        public PotMembership Membership { get; set; } = null!;

        /// <summary>In the pot's currency.</summary>
        public decimal Amount { get; set; }

        public DateOnly Date { get; set; }

        public string? Note { get; set; }

        /// <summary>
        /// Any account the withdrawing member owns. A bare Guid with no foreign
        /// key — see <see cref="PotTopUp.SourceAccountId"/> for why a pot row
        /// never points at a user-owned one.
        /// </summary>
        public Guid DestinationAccountId { get; set; }
    }
}
