using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    /// <summary>
    /// One browser or installed PWA that has agreed to receive push notifications,
    /// as handed over by the Push API's <c>PushSubscription</c>.
    /// <para>
    /// A row per device, not per user: the same account signed in on a phone and a
    /// laptop is two subscriptions, and revoking one must not silence the other.
    /// The three fields that matter are the push service's
    /// <see cref="Endpoint"/> (where to POST) and the pair
    /// <see cref="P256dh"/>/<see cref="Auth"/>, which are the browser's public key
    /// and shared secret — the payload is encrypted to them, so the push service
    /// itself never sees what the message says (RFC 8291).
    /// </para>
    /// <para>
    /// The keys are the browser's, not ours: they authorise nothing, cannot be
    /// used to read anything, and are useless without the endpoint. They are
    /// still user data, so they cascade away with the account like everything
    /// else a user owns.
    /// </para>
    /// </summary>
    public class PushSubscription : BaseEntity
    {
        [SetsRequiredMembers]
        public PushSubscription() : base()
        {
            UserId = null!;
            Endpoint = null!;
            P256dh = null!;
            Auth = null!;
        }

        /// <summary>
        /// The push service URL to POST to — fcm.googleapis.com,
        /// web.push.apple.com, updates.push.services.mozilla.com. Unique across
        /// the table: the browser mints one per registration, so a re-subscribe
        /// after a re-install (or by a different account on a shared device)
        /// must move the row rather than duplicate it.
        /// </summary>
        public required string Endpoint { get; set; }

        /// <summary>The subscriber's P-256 public key, base64url, uncompressed point (65 bytes).</summary>
        public required string P256dh { get; set; }

        /// <summary>The subscriber's 16-byte authentication secret, base64url.</summary>
        public required string Auth { get; set; }

        /// <summary>
        /// Whatever the browser called itself when it subscribed, so the settings
        /// screen can say "Firefox on Linux" instead of listing opaque URLs.
        /// Truncated on the way in; never parsed.
        /// </summary>
        public string? UserAgent { get; set; }

        public DateTime? LastSuccessAtUtc { get; set; }
        public DateTime? LastFailureAtUtc { get; set; }

        /// <summary>
        /// Consecutive delivery failures that were <em>not</em> a definitive
        /// 404/410 (those delete the row outright). A transient outage should not
        /// cost someone their subscription, so only a run of them does.
        /// </summary>
        public int FailureCount { get; set; }
    }
}
