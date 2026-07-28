namespace ExpenseTracker.Shared.Enums
{
    /// <summary>
    /// Which axis a category belongs to.
    /// <para>
    /// There is deliberately no "planned" type: a planned expense uses the
    /// same <see cref="Expense"/> categories a hand-typed one does, so that
    /// committing it lands in the budget and the report the user already has
    /// set up. The retired value 3 (FutureExpense) is folded into
    /// <see cref="Expense"/> by the PlannedTransactions migration.
    /// </para>
    /// </summary>
    public enum CategoryType
    {
        Expense = 0,
        Income = 1,
        SavingGoals = 2
    }
}
