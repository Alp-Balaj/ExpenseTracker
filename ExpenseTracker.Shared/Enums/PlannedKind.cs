namespace ExpenseTracker.Shared.Enums
{
    /// <summary>
    /// What a planned transaction turns into when it is committed. Wider than
    /// <see cref="TransactionKind"/> (which only describes the report axis)
    /// because a standing order into savings is a transfer, not a spend.
    /// </summary>
    public enum PlannedKind
    {
        Expense = 0,
        Income = 1,
        Transfer = 2
    }
}
