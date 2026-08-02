using System.ComponentModel.DataAnnotations;
using ExpenseTracker.Application.Validation;

namespace ExpenseTracker.Application.DTOs
{
    /// <summary>
    /// Everything the server needs to read one bank's CSV the way that bank writes
    /// it — the "profile" of Sugg-6, minus the persistence.
    /// <para>
    /// Deliberately not an entity. The note proposed storing named profiles
    /// server-side, but Stream G is a no-migration stream (the migration lock is
    /// held by P → M → O), and a profile is settings rather than financial data:
    /// the SPA keeps them in local storage and posts one here. Promoting them to a
    /// row is a later, additive change that needs nothing here to move.
    /// </para>
    /// </summary>
    public sealed class ImportMappingDTO
    {
        /// <summary>Account every imported row is booked against.</summary>
        [NotDefault]
        public Guid AccountId { get; set; }

        /// <summary>Column indexes, zero-based, into the parsed row.</summary>
        public int DateColumn { get; set; } = -1;
        public int AmountColumn { get; set; } = -1;
        public int DescriptionColumn { get; set; } = -1;

        /// <summary>Optional: who the money went to or came from, when the bank separates it.</summary>
        public int? CounterpartyColumn { get; set; }

        public bool HasHeaderRow { get; set; } = true;

        /// <summary>Leading junk lines before the header — several banks emit an account summary first.</summary>
        [Range(0, 50)]
        public int SkipRows { get; set; }

        /// <summary>Single character; auto-detected when null or empty.</summary>
        [MaxLength(1)]
        public string? Delimiter { get; set; }

        /// <summary>
        /// Culture used for the decimal separator and for resolving an ambiguous
        /// <c>03/04/2026</c>. This is the field that decides whether a German
        /// statement imports as thousands or as cents.
        /// </summary>
        [MaxLength(20)]
        public string Culture { get; set; } = "en-US";

        /// <summary>Explicit date format, when the culture's own patterns aren't enough.</summary>
        [MaxLength(40)]
        public string? DateFormat { get; set; }

        /// <summary>
        /// True (the usual bank convention): a negative amount is money leaving the
        /// account. False flips it, for exports that state debits as positives.
        /// </summary>
        public bool NegativeIsExpense { get; set; } = true;

        [NotDefault]
        public Guid DefaultExpenseCategoryId { get; set; }

        [NotDefault]
        public Guid DefaultIncomeCategoryId { get; set; }

        /// <summary>
        /// "REWE" → Groceries. Applied in order, first match wins, against the
        /// description and counterparty together.
        /// </summary>
        [MaxLength(100)]
        public List<ImportCategoryRuleDTO> Rules { get; set; } = [];

        /// <summary>True once the three required columns and both defaults are chosen.</summary>
        public bool IsComplete =>
            AccountId != Guid.Empty
            && DateColumn >= 0
            && AmountColumn >= 0
            && DescriptionColumn >= 0
            && DefaultExpenseCategoryId != Guid.Empty
            && DefaultIncomeCategoryId != Guid.Empty;
    }

    public sealed class ImportCategoryRuleDTO
    {
        [Required, MaxLength(100)]
        public string Contains { get; set; } = string.Empty;

        [NotDefault]
        public Guid CategoryId { get; set; }
    }

    /// <summary>What one line of the file would become.</summary>
    public sealed class ImportPreviewRowDTO
    {
        /// <summary>Line number in the original file, so a problem row can be found in a spreadsheet.</summary>
        public int RowNumber { get; init; }

        public DateOnly? Date { get; init; }

        /// <summary>Always positive; the direction is carried by <see cref="IsExpense"/>.</summary>
        public decimal Amount { get; init; }

        public bool IsExpense { get; init; }

        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }

        public Guid CategoryId { get; init; }
        public string CategoryName { get; init; } = string.Empty;

        /// <summary>True when the rule list picked the category rather than the default.</summary>
        public bool CategoryFromRule { get; init; }

        /// <summary>
        /// Matches something already in the account. Skipped on commit — importing
        /// the same statement twice is the normal way this feature gets used.
        /// </summary>
        public bool IsDuplicate { get; init; }

        /// <summary>Why this row cannot be imported, or null when it can.</summary>
        public string? Problem { get; init; }

        public bool CanImport => Problem is null && !IsDuplicate;

        /// <summary>The original cells, so the preview can show what it read.</summary>
        public string[] Raw { get; init; } = [];
    }

    public sealed class ImportPreviewDTO
    {
        /// <summary>Header cells, or synthesised "Column 1..N" when the file has no header.</summary>
        public IReadOnlyList<string> Columns { get; init; } = [];

        public string Delimiter { get; init; } = ",";

        /// <summary>A best guess at the mapping, for the screen to open with.</summary>
        public ImportSuggestedMappingDTO Suggested { get; init; } = new();

        /// <summary>Empty until the mapping is complete — the file is inspected first, mapped second.</summary>
        public IReadOnlyList<ImportPreviewRowDTO> Rows { get; init; } = [];

        public int TotalRows { get; init; }
        public int ReadyCount { get; init; }
        public int DuplicateCount { get; init; }
        public int ProblemCount { get; init; }

        /// <summary>True when the file was longer than the import ceiling and was cut.</summary>
        public bool Truncated { get; init; }
    }

    public sealed class ImportSuggestedMappingDTO
    {
        public int? DateColumn { get; init; }
        public int? AmountColumn { get; init; }
        public int? DescriptionColumn { get; init; }
        public int? CounterpartyColumn { get; init; }
    }

    /// <summary>What the commit actually did — the sentence the user is shown.</summary>
    public sealed class ImportResultDTO
    {
        public int Imported { get; init; }
        public int DuplicatesSkipped { get; init; }
        public int Failed { get; init; }

        public decimal ExpenseTotal { get; init; }
        public decimal IncomeTotal { get; init; }

        /// <summary>Rows that could not be imported, so nothing disappears silently.</summary>
        public IReadOnlyList<ImportProblemDTO> Problems { get; init; } = [];
    }

    public sealed class ImportProblemDTO
    {
        public int RowNumber { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string[] Raw { get; init; } = [];
    }
}
