using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces
{
    public interface ICashflowProjectionService
    {
        /// <summary>
        /// Projects the caller's liquid balance day by day over the next month,
        /// combining current account balances, the queue of pending planned
        /// occurrences, the templates' future due dates and the everyday burn
        /// rate observed so far this month. Pure computation; nothing is stored.
        /// </summary>
        Task<CashflowProjectionDTO> GetProjectionAsync(CancellationToken cancellationToken = default);
    }
}
