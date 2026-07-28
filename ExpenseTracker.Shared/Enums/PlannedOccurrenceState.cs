namespace ExpenseTracker.Shared.Enums
{
    /// <summary>
    /// Where one due date of a planned transaction has got to.
    /// <para>
    /// Nothing posts unattended: an occurrence is a reminder that sits in
    /// <see cref="Pending"/> until the user confirms it, because the due date
    /// is a prediction and the money rarely moves on exactly that day. Only
    /// the confirmation knows the real date, the real amount and the account
    /// it actually came from.
    /// </para>
    /// </summary>
    public enum PlannedOccurrenceState
    {
        Pending = 0,
        Committed = 1,
        Skipped = 2
    }
}
