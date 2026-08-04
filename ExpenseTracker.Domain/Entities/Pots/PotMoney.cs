namespace ExpenseTracker.Domain.Entities.Pots
{
    /// <summary>
    /// What one pot looks like on a list: the cash in it, and where the reader
    /// stands against it.
    /// <para>
    /// Both are summed from rows rather than stored, which is why there is no
    /// concurrency token anywhere in this cycle's money model and nothing that
    /// can drift out of step. <c>Σ</c> of every member's
    /// <see cref="YourPosition"/> equals <see cref="Balance"/> — the invariant
    /// the whole wave exists to keep true, read from the cheap end.
    /// </para>
    /// </summary>
    /// <param name="Balance">
    /// Top-ups, less what the pot itself paid for, less withdrawals. In the
    /// pot's currency, never converted.
    /// </param>
    /// <param name="YourPosition">
    /// <c>topups + outOfPocket − shares − withdrawals</c> for the reader.
    /// Positive means the pot holds money that is still theirs.
    /// </param>
    public readonly record struct PotMoney(decimal Balance, decimal YourPosition);
}
