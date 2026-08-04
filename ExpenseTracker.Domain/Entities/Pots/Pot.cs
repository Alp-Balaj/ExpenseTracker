using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.Pots
{
    /// <summary>
    /// A standing group with a shared wallet — household, flat, trip. A user may
    /// belong to any number of them.
    /// <para>
    /// A pot is not owned by anyone: <see cref="PotEntity"/> carries no UserId,
    /// and <see cref="CreatedByUserId"/> below records who opened it without
    /// making them its owner. Every member has the same rights (flat trust), and
    /// the activity log rather than a permission matrix is what makes that
    /// safe.
    /// </para>
    /// <para>
    /// There is no money here yet. Wave W1 ships the group; the balance, the
    /// positions and the ledger arrive in W2.
    /// </para>
    /// </summary>
    public class Pot : PotEntity
    {
        public required string Name { get; set; }

        /// <summary>
        /// The pot's currency, as an ISO code and not as a <see cref="Currency"/>
        /// row. Currency rows are user-owned — each member has their own, with
        /// their own rates — so a pot can only name a code. Fixed at creation:
        /// changing it would silently reinterpret every amount W2 goes on to
        /// write.
        /// </summary>
        public required string CurrencyCode { get; set; }

        /// <summary>
        /// The balance at which the pot is considered low, in
        /// <see cref="CurrencyCode"/>. Set at creation with a default, editable
        /// by any member, and switched off by setting it to zero. Nothing reads
        /// it in W1 — the alert that does is W4's — but it is decided at
        /// creation, so it is stored at creation.
        /// </summary>
        public decimal LowBalanceThreshold { get; set; }

        /// <summary>
        /// Who opened the pot. Nullable with <c>OnDelete(Restrict)</c>, for the
        /// same reason <see cref="PotMembership.UserId"/> is: anonymizing nulls
        /// it, and a raw user delete that skipped the anonymize step fails at the
        /// foreign key instead of cascading a group's history away. The pot goes
        /// on displaying its creator through their membership's frozen label.
        /// </summary>
        public string? CreatedByUserId { get; set; }

        public ICollection<PotMembership> Memberships { get; set; } = new List<PotMembership>();
        public ICollection<PotInvite> Invites { get; set; } = new List<PotInvite>();
        public ICollection<PotActivity> Activity { get; set; } = new List<PotActivity>();
    }
}
