using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// A note that one particular notification has already been sent, so it is
    /// never sent again.
    /// <para>
    /// This is what stops the feature from destroying itself. The jobs are
    /// catch-up by design and re-evaluate the same state on every run, so
    /// "Groceries is at 80%" is true every single day for the rest of the month.
    /// Without a record of what has already gone out, a budget crossed on the 5th
    /// would push twenty-six more times — and the roadmap's own warning is that
    /// noise is how you lose the permission.
    /// </para>
    /// <para>
    /// <see cref="DedupeKey"/> carries the identity of the *event*, not of the
    /// message: <c>{budgetId}:2026-08:80</c> says "this budget, this month, this
    /// threshold". Crossing 100% later in the same month has a different key and
    /// is genuinely worth saying.
    /// </para>
    /// </summary>
    public class NotificationDelivery : BaseEntity
    {
        [SetsRequiredMembers]
        public NotificationDelivery() : base()
        {
            UserId = null!;
            DedupeKey = null!;
        }

        public required NotificationKind Kind { get; set; }

        /// <summary>
        /// Identifies the event within its <see cref="Kind"/>, uniquely per user.
        /// Always contains the period it belongs to, so the key set stays bounded
        /// and last month's crossings never suppress this month's.
        /// </summary>
        public required string DedupeKey { get; set; }

        public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>How many devices the message actually reached. Diagnostic only.</summary>
        public int DeviceCount { get; set; }
    }
}
