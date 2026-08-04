using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities.Pots
{
    /// <summary>
    /// One person's place in one pot — and the row every later wave attributes
    /// money to, which is why it is allowed to outlive the person.
    /// </summary>
    public class PotMembership : PotEntity, IPotScoped
    {
        public Guid PotId { get; set; }
        public Pot Pot { get; set; } = null!;

        /// <summary>
        /// The Identity user behind this membership, or null once it has been
        /// anonymized.
        /// <para>
        /// Nullable with <c>OnDelete(Restrict)</c>, and that combination is the
        /// backstop the whole deletion story rests on. Anonymizing sets this to
        /// null and freezes <see cref="DisplayName"/>; the row, and everything
        /// pointing at it, survives. A user delete that skipped the anonymize
        /// step does not quietly cascade the group's history away — it fails
        /// loudly at the foreign key, surfacing as a
        /// <c>DbUpdateException</c> (409). <b>That is the intended behaviour,
        /// not a bug to be fixed by loosening the FK.</b> The supported path is
        /// anonymize, then delete, and <c>UserController.DeleteAccount</c> walks
        /// it in that order.
        /// </para>
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// What the other members see. Copied from the user's name when they
        /// join rather than joined at read time, so it still reads sensibly
        /// after the account behind it is gone — at which point anonymizing
        /// replaces it with a neutral label.
        /// </summary>
        public required string DisplayName { get; set; }

        /// <summary>
        /// Null while the membership is live. Leaving stamps it rather than
        /// deleting the row: the activity log points here, W2's largest-remainder
        /// tie-break is ordered on <see cref="PotEntity.CreatedAt"/>, and both
        /// would break if a departure could erase a member. Rejoining the same
        /// pot clears it and reuses this row, which is also what keeps the
        /// tie-break order stable and redemption idempotent.
        /// </summary>
        public DateTime? LeftAt { get; set; }

        /// <summary>When the row was stripped of its user, if it has been.</summary>
        public DateTime? AnonymizedAt { get; set; }

        public bool IsActive => LeftAt is null;
        public bool IsAnonymized => AnonymizedAt is not null;
    }
}
