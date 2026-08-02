namespace ExpenseTracker.Shared.Enums
{
    /// <summary>
    /// The only reasons this app is allowed to interrupt someone (decision D3).
    /// <para>
    /// Kept as a closed list on purpose. Every extra trigger is one more chance
    /// for a push to be noise, and a notification permission is revoked once and
    /// never granted again — so the cost of a fourth reason is losing the three
    /// that mattered.
    /// </para>
    /// </summary>
    public enum NotificationKind
    {
        /// <summary>A budget crossed a threshold it had not crossed this month.</summary>
        BudgetThreshold = 0,

        /// <summary>A planned transaction comes due tomorrow.</summary>
        PlannedDue = 1,

        /// <summary>The weekly digest: what last week cost, against the week before.</summary>
        WeeklyDigest = 2,

        /// <summary>
        /// Sent only by the user pressing "Send a test" in settings. Not a
        /// trigger — it exists so a real device can prove the round trip.
        /// </summary>
        Test = 3
    }
}
