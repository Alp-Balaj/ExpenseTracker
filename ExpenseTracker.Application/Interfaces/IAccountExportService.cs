using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// C·4: everything the current user owns, in a form they can take away.
    /// <para>
    /// The counterweight to Suggestion-6's import, and the thing that makes
    /// trusting this app with years of financial history a reversible decision.
    /// </para>
    /// </summary>
    public interface IAccountExportService
    {
        /// <summary>The complete snapshot — every table, with ids so relationships survive.</summary>
        Task<AccountExportDTO> ExportAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The transactions alone, flattened into one spreadsheet-friendly sheet.
        /// Lossier than the JSON on purpose: this is the one people actually open.
        /// </summary>
        Task<string> ExportTransactionsCsvAsync(CancellationToken cancellationToken = default);
    }
}
