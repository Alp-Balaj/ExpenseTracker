namespace ExpenseTracker.Shared.Enums
{
    /// <summary>
    /// How often a planned transaction comes round. <see cref="Once"/> is a
    /// single future-dated payment — the thing FutureExpense used to model —
    /// and is deliberately part of the same enum so a one-off and a
    /// subscription are the same row with a different cadence.
    /// <para>
    /// The anchor (day of week, day of month, month + day) is taken from the
    /// template's StartDate rather than stored separately, so there is only
    /// one place the schedule can disagree with itself.
    /// </para>
    /// </summary>
    public enum Recurrence
    {
        Once = 0,
        Weekly = 1,
        Monthly = 2,
        Yearly = 3
    }
}
