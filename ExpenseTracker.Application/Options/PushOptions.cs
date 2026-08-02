namespace ExpenseTracker.Application.Options
{
    /// <summary>
    /// Binds the "Push" configuration section: the VAPID identity this server
    /// signs with (RFC 8292).
    /// <para>
    /// <see cref="PrivateKey"/> is a real secret and must arrive as a Railway
    /// environment variable (<c>Push__PrivateKey</c>) — never appsettings, never
    /// source control. <c>scripts/generate-vapid-keys.sh</c> produces a pair.
    /// With no keys configured the whole feature reports itself unavailable and
    /// the notification jobs skip: an unconfigured deployment is silent, not
    /// broken.
    /// </para>
    /// </summary>
    public sealed class PushOptions
    {
        public const string SectionName = "Push";

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The VAPID public key, base64url, uncompressed P-256 point (65 bytes).
        /// Handed to the browser as <c>applicationServerKey</c>, so it is public
        /// by definition.
        /// </summary>
        public string PublicKey { get; set; } = string.Empty;

        /// <summary>The matching P-256 private scalar, base64url (32 bytes). Secret.</summary>
        public string PrivateKey { get; set; } = string.Empty;

        /// <summary>
        /// Contact for whoever runs this push server, as a mailto: or https: URL.
        /// Push services require it and use it to reach an operator whose sender
        /// is misbehaving before they block it.
        /// </summary>
        public string Subject { get; set; } = "mailto:admin@expensetracker.local";

        /// <summary>How long a push service should hold an undelivered message for.</summary>
        public int TimeToLiveSeconds { get; set; } = 12 * 3600;

        /// <summary>Per-request timeout when POSTing to a push service.</summary>
        public double TimeoutSeconds { get; set; } = 15;

        /// <summary>
        /// Consecutive transient failures after which a subscription is dropped.
        /// A definitive 404/410 drops it immediately; this only covers endpoints
        /// that keep timing out.
        /// </summary>
        public int MaxConsecutiveFailures { get; set; } = 5;

        /// <summary>
        /// True when there is enough configuration to actually send. Everything
        /// downstream — the jobs, the controller, the SPA's settings screen —
        /// reads this rather than testing the keys itself.
        /// </summary>
        public bool IsConfigured =>
            Enabled
            && !string.IsNullOrWhiteSpace(PublicKey)
            && !string.IsNullOrWhiteSpace(PrivateKey);

        public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 120));
        public int TimeToLive => Math.Clamp(TimeToLiveSeconds, 0, 28 * 24 * 3600);
    }
}
