using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// Sends a notification to every device a user has registered — once, ever,
    /// for a given event.
    /// <para>
    /// Deliberately takes an explicit user id: the jobs that call it run with no
    /// HTTP context and sweep every user, exactly like
    /// <see cref="IExchangeRateUpdater.UpdateAllUsersAsync"/>.
    /// </para>
    /// </summary>
    public interface IPushNotificationService
    {
        /// <summary>
        /// Delivers <paramref name="message"/> to all of the user's subscriptions,
        /// unless a delivery with the same <paramref name="kind"/> and
        /// <paramref name="dedupeKey"/> has already been recorded for them.
        /// <para>
        /// The dedupe record is written <em>before</em> sending and inside the same
        /// unique index that guards it, so the failure mode of a crash mid-send is
        /// a missed notification rather than a repeated one. For a budget warning
        /// that is the right way round.
        /// </para>
        /// </summary>
        /// <returns>How many devices the message actually reached; 0 if it was a duplicate.</returns>
        Task<int> SendToUserAsync(
            string userId,
            NotificationKind kind,
            string dedupeKey,
            PushMessage message,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends to every device without touching the dedupe log — the "send a
        /// test" button, which has to work twice in a row to be any use.
        /// </summary>
        Task<int> SendUndeduplicatedAsync(
            string userId, PushMessage message, CancellationToken cancellationToken = default);
    }
}
