namespace ExpenseTracker.Shared.Enums
{
    /// <summary>
    /// What one line of a pot's money feed is. The three writes W2 ships, read
    /// back as one ordered list — a member reading a pot wants "what happened to
    /// the money", not three separate lists they have to interleave by eye.
    /// </summary>
    public enum PotLedgerEntryKind
    {
        TopUp = 0,
        Expense = 1,
        Withdrawal = 2,
    }
}
