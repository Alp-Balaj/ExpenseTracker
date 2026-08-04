using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities.Pots
{
    /// <summary>
    /// One line of the group's visible history: who did what to which row, and
    /// what it looked like before and after.
    /// <para>
    /// Trust inside a pot is flat — any member may act on anything — so this log
    /// is the accountability. Every member-visible mutation writes one of these
    /// in the same transaction as the change itself; a change that skipped it
    /// would be a change nobody can see.
    /// </para>
    /// </summary>
    public class PotActivity : PotEntity, IPotScoped
    {
        public Guid PotId { get; set; }
        public Pot Pot { get; set; } = null!;

        /// <summary>
        /// The member who did it, as a membership so the attribution survives
        /// their account. Nullable only to leave room for a future system-issued
        /// entry; everything W1 writes has a real actor.
        /// </summary>
        public Guid? ActorMembershipId { get; set; }
        public PotMembership? ActorMembership { get; set; }

        public PotActivityKind Kind { get; set; }

        /// <summary>The row the action was about — a membership, an invite, or the pot itself.</summary>
        public Guid? TargetId { get; set; }

        /// <summary>
        /// How to name that row to a human, frozen at write time. The alternative
        /// is resolving it at read time, which would make the log lie about what
        /// something was called when it happened.
        /// </summary>
        public string? TargetLabel { get; set; }

        /// <summary>
        /// The before and after, rendered for display rather than stored as a
        /// structured diff. W1's mutations change one scalar at a time, and a
        /// JSON patch nobody can read is not accountability. Both are null for
        /// actions that create or remove rather than change.
        /// </summary>
        public string? Before { get; set; }
        public string? After { get; set; }

        // The timestamp the contract asks for is CreatedAt: the log is
        // append-only, so when the row was written is when it happened, and a
        // second column would only be a second thing to keep in step.
    }
}
