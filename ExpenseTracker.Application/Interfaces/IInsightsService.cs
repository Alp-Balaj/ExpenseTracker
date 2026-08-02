using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces
{
    public interface IInsightsService
    {
        /// <summary>
        /// Compares the caller's current-month spending with the previous three
        /// months — overall and per category — so the dashboard can point out
        /// what moved. Pure computation over existing expenses; nothing is stored.
        /// </summary>
        Task<InsightsDTO> GetInsightsAsync(CancellationToken cancellationToken = default);
    }
}
