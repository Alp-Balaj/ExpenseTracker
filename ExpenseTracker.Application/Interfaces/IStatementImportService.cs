using ExpenseTracker.Application.DTOs;

namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// Sugg-6's two-phase CSV import: look at the file, then commit it.
    /// <para>
    /// Two phases because bank CSVs are messy and a silent misimport destroys
    /// trust in every number the app shows afterwards. Nothing is written until
    /// the user has seen exactly which rows would be created, which are
    /// duplicates, and which cannot be read at all.
    /// </para>
    /// <para>
    /// Both phases parse the same file through the same code, so the preview is a
    /// promise rather than an estimate — the only way the commit can differ is if
    /// the underlying data changed between the two calls, which is why duplicate
    /// detection runs again at commit time.
    /// </para>
    /// </summary>
    public interface IStatementImportService
    {
        /// <summary>
        /// Reads the file and reports what it would do. With an incomplete mapping
        /// this still returns the columns and a suggested mapping, so the upload
        /// and the mapping are one round trip apart rather than two uploads.
        /// </summary>
        Task<ImportPreviewDTO> PreviewAsync(
            Stream csv, ImportMappingDTO mapping, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the importable rows, in one database transaction, through the
        /// same balance-adjusting path a hand-typed expense takes (Fix-2). An
        /// import is many creates, not a bulk insert that bypasses the rules.
        /// </summary>
        Task<ImportResultDTO> CommitAsync(
            Stream csv, ImportMappingDTO mapping, CancellationToken cancellationToken = default);
    }
}
