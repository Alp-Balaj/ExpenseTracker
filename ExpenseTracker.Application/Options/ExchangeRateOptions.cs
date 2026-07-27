namespace ExpenseTracker.Application.Options
{
    /// <summary>
    /// Binds the "ExchangeRates" configuration section. Defaults target
    /// frankfurter.app: free, no API key, ECB reference rates — enough for a
    /// personal tracker, and the reason no secret is involved anywhere here.
    /// </summary>
    public sealed class ExchangeRateOptions
    {
        public const string SectionName = "ExchangeRates";

        /// <summary>
        /// Turns the daily background refresh on and off. The manual refresh endpoint
        /// is unaffected — this only governs the unattended sweep. Set false in the
        /// test suite so no test ever depends on the network.
        /// </summary>
        public bool Enabled { get; set; } = true;

        public string BaseUrl { get; set; } = "https://api.frankfurter.app/";

        public double IntervalHours { get; set; } = 24;

        /// <summary>
        /// Keeps the first sweep off the critical path of a cold start (Railway boots
        /// the container and starts serving immediately).
        /// </summary>
        public double StartupDelaySeconds { get; set; } = 10;

        public double TimeoutSeconds { get; set; } = 15;

        // Clamped so a typo like "IntervalHours": 0 can't turn the sweep into a
        // hot loop hammering a free public API.
        public TimeSpan Interval => TimeSpan.FromHours(Math.Clamp(IntervalHours, 1, 24 * 30));
        public TimeSpan StartupDelay => TimeSpan.FromSeconds(Math.Clamp(StartupDelaySeconds, 0, 3600));
        public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 120));
    }
}
