namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>Where one encrypted message is delivered: the browser's own subscription triple.</summary>
    public readonly record struct PushTarget(string Endpoint, string P256dh, string Auth);

    /// <summary>
    /// What the user sees. Kept to the fields every platform honours — Safari
    /// silently drops the rest — plus the path to open, which is the entire
    /// point of a notification about money.
    /// </summary>
    /// <param name="Tag">
    /// Collapses same-subject notifications on the device: a second budget
    /// warning replaces the first in the tray instead of stacking under it.
    /// </param>
    public sealed record PushMessage(string Title, string Body, string Url, string Tag);

    public enum PushSendOutcome
    {
        /// <summary>The push service accepted it (2xx).</summary>
        Delivered = 0,

        /// <summary>
        /// 404 or 410: the subscription is definitively dead — the browser was
        /// uninstalled, the permission revoked, the endpoint rotated. The row is
        /// deleted, not retried.
        /// </summary>
        Gone = 1,

        /// <summary>Anything else. Worth retrying; too many in a row and the row goes.</summary>
        TransientFailure = 2,

        /// <summary>No VAPID keys configured, so nothing was attempted.</summary>
        NotConfigured = 3
    }

    public readonly record struct PushSendResult(PushSendOutcome Outcome, int StatusCode, string? Detail)
    {
        public bool Delivered => Outcome == PushSendOutcome.Delivered;
    }

    /// <summary>
    /// The wire half of Web Push: VAPID-sign the request (RFC 8292), encrypt the
    /// payload to the subscriber's keys (RFC 8291), POST it to the push service.
    /// <para>
    /// One message, one device, no policy — who gets told what, and whether they
    /// have been told already, is <see cref="IPushNotificationService"/>'s job.
    /// </para>
    /// </summary>
    public interface IPushSender
    {
        /// <summary>False when no VAPID keys are configured; every send then no-ops.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// The VAPID public key the browser must pass as
        /// <c>applicationServerKey</c> when it subscribes. Null when unconfigured.
        /// </summary>
        string? PublicKey { get; }

        Task<PushSendResult> SendAsync(
            PushTarget target, PushMessage message, CancellationToken cancellationToken = default);
    }
}
