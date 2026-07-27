namespace ExpenseTracker.Shared.Enums
{
    // Only monthly budgets exist today. The enum is here so weekly/yearly can
    // be added without a data migration on the Budget rows already stored.
    public enum BudgetPeriod
    {
        Monthly = 0
    }
}
