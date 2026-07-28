using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces;
public interface ICategoryService
{
    /// <summary>
    /// Every category the caller owns, of every <see cref="Shared.Enums.CategoryType"/>,
    /// with its totals. One call rather than one per type: the four types share
    /// the category list and the exchange-rate snapshot, and re-reading them per
    /// type is what made the dashboard's category widget the most expensive
    /// request in the app.
    /// </summary>
    Task<IReadOnlyList<CategorySummaryDTO>> GetCategorySummariesAsync(CancellationToken cancellationToken = default);
}
