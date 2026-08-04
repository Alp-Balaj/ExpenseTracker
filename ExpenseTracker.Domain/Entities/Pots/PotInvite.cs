using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.Pots
{
    /// <summary>
    /// A link that lets someone join a pot. Joining is opt-in — nobody is ever
    /// added to a group without redeeming one of these themselves.
    /// </summary>
    public class PotInvite : PotEntity, IPotScoped
    {
        public Guid PotId { get; set; }
        public Pot Pot { get; set; } = null!;

        /// <summary>
        /// The secret in the link. High-entropy and uniquely indexed, because it
        /// is the one pot value that is looked up without a membership behind it
        /// — redeeming is how a stranger becomes a member, so that read cannot be
        /// membership-scoped and the code itself has to be the whole guard.
        /// </summary>
        public required string Code { get; set; }

        /// <summary>
        /// Who issued it, as a membership rather than a user id, so the issuer is
        /// still named after their account is gone.
        /// </summary>
        public Guid IssuedByMembershipId { get; set; }
        public PotMembership IssuedByMembership { get; set; } = null!;

        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Stamped when any member revokes it — flat trust, so any member may
        /// revoke any invite, including one they did not issue.
        /// </summary>
        public DateTime? RevokedAt { get; set; }
        public Guid? RevokedByMembershipId { get; set; }
        public PotMembership? RevokedByMembership { get; set; }

        public bool IsRevoked => RevokedAt is not null;
        public bool HasExpired(DateTime asOf) => ExpiresAt <= asOf;

        /// <summary>
        /// Redeemable by anyone holding the code. Deliberately not single-use: an
        /// invite link is a thing you paste into a group chat, and expiry plus
        /// revocation are what bound it.
        /// </summary>
        public bool IsRedeemable(DateTime asOf) => !IsRevoked && !HasExpired(asOf);
    }
}
