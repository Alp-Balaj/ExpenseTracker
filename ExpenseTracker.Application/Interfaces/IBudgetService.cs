using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces
{
    public interface IBudgetService
    {
        /// <summary>
        /// Evaluates every budget the caller owns against the spend recorded in
        /// <paramref name="month"/>. Budgets recur, so the same rows are re-evaluated
        /// for whichever month is asked for — including past ones.
        /// </summary>
        Task<IReadOnlyList<BudgetStatusDTO>> GetStatusAsync(int year, int month, CancellationToken cancellationToken = default);
    }
}
