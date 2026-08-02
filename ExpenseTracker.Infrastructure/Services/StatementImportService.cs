using System.Globalization;
using System.Text;
using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Services;

/// <inheritdoc cref="IStatementImportService"/>
/// <remarks>
/// In Infrastructure because both phases need the database for the thing that
/// makes the feature usable twice: duplicate detection reads the account's
/// existing rows, and the commit writes many of them inside one transaction with
/// a single balance adjustment.
/// </remarks>
public sealed class StatementImportService : IStatementImportService
{
    /// <summary>Rows past this are ignored. A personal statement is hundreds of lines, not thousands.</summary>
    public const int MaxRows = 5000;

    /// <summary>How many preview rows are returned. The counts above the table cover the rest.</summary>
    private const int MaxPreviewRows = 500;

    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccountServices _accountServices;
    private readonly ICurrentUserServices _currentUser;
    private readonly ILogger<StatementImportService> _logger;

    public StatementImportService(
        ApplicationDbContext context,
        IUnitOfWork unitOfWork,
        IAccountServices accountServices,
        ICurrentUserServices currentUser,
        ILogger<StatementImportService> logger)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _accountServices = accountServices;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ImportPreviewDTO> PreviewAsync(
        Stream csv, ImportMappingDTO mapping, CancellationToken cancellationToken = default)
    {
        var file = await ReadAsync(csv, mapping, cancellationToken);

        if (!mapping.IsComplete)
        {
            // Phase one: the user has uploaded a file and has not mapped it yet.
            // Answer with what the file looks like and a guess, so mapping is an
            // edit rather than an interrogation.
            return new ImportPreviewDTO
            {
                Columns = file.Columns,
                Delimiter = file.Delimiter.ToString(),
                Suggested = file.Suggested,
                TotalRows = file.DataRows.Count,
                Truncated = file.Truncated
            };
        }

        var rows = await BuildRowsAsync(file, mapping, cancellationToken);

        return new ImportPreviewDTO
        {
            Columns = file.Columns,
            Delimiter = file.Delimiter.ToString(),
            Suggested = file.Suggested,
            Rows = rows.Take(MaxPreviewRows).ToList(),
            TotalRows = rows.Count,
            ReadyCount = rows.Count(r => r.CanImport),
            DuplicateCount = rows.Count(r => r.IsDuplicate),
            ProblemCount = rows.Count(r => r.Problem is not null),
            Truncated = file.Truncated
        };
    }

    public async Task<ImportResultDTO> CommitAsync(
        Stream csv, ImportMappingDTO mapping, CancellationToken cancellationToken = default)
    {
        if (!mapping.IsComplete)
            throw new ArgumentException("The mapping is incomplete.", nameof(mapping));

        var userId = _currentUser.UserId;
        var file = await ReadAsync(csv, mapping, cancellationToken);

        // Re-parsed and re-checked for duplicates rather than trusting the preview
        // the client is holding: between the two calls the user may well have been
        // in another tab creating one of these rows by hand.
        var rows = await BuildRowsAsync(file, mapping, cancellationToken);

        var importable = rows.Where(r => r.CanImport).ToList();
        var problems = rows
            .Where(r => r.Problem is not null)
            .Select(r => new ImportProblemDTO { RowNumber = r.RowNumber, Reason = r.Problem!, Raw = r.Raw })
            .ToList();

        var duplicates = rows.Count(r => r.IsDuplicate);

        if (importable.Count == 0)
        {
            return new ImportResultDTO
            {
                Imported = 0,
                DuplicatesSkipped = duplicates,
                Failed = problems.Count,
                Problems = problems
            };
        }

        var expenseTotal = importable.Where(r => r.IsExpense).Sum(r => r.Amount);
        var incomeTotal = importable.Where(r => !r.IsExpense).Sum(r => r.Amount);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // One net adjustment through the same IAccountServices path a hand-typed
        // expense takes (Fix-2), rather than one atomic UPDATE per row: five
        // hundred round trips to reach the same number is not more correct, and
        // the whole import is one transaction either way. The service is still
        // what enforces that the account exists and belongs to the caller.
        var delta = incomeTotal - expenseTotal;
        if (delta != 0m)
        {
            var (success, error) = await _accountServices.AdjustAccountBalance(delta, mapping.AccountId, cancellationToken);
            if (!success) throw new InvalidOperationException(error);
        }

        foreach (var row in importable)
        {
            if (row.IsExpense)
            {
                _context.Expenses.Add(new Expense
                {
                    UserId = userId,
                    Title = Truncate(row.Title, 100) ?? "Imported",
                    Amount = row.Amount,
                    Date = row.Date!.Value,
                    Description = Truncate(row.Description, 500),
                    AccountId = mapping.AccountId,
                    CategoryId = row.CategoryId,
                    Account = null!,
                    Category = null!
                });
            }
            else
            {
                // Income has no Title of its own — the statement's memo line is its
                // description, which is what every income list shows.
                _context.Incomes.Add(new Income
                {
                    UserId = userId,
                    Amount = row.Amount,
                    Date = row.Date!.Value,
                    Description = Truncate(row.Title, 500),
                    AccountId = mapping.AccountId,
                    CategoryId = row.CategoryId,
                    Account = null!,
                    Category = null!
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Imported {Count} row(s) for user {UserId} into account {AccountId} ({Duplicates} duplicate(s) skipped, {Failed} failed).",
            importable.Count, userId, mapping.AccountId, duplicates, problems.Count);

        return new ImportResultDTO
        {
            Imported = importable.Count,
            DuplicatesSkipped = duplicates,
            Failed = problems.Count,
            ExpenseTotal = expenseTotal,
            IncomeTotal = incomeTotal,
            Problems = problems
        };
    }

    // ── Reading ────────────────────────────────────────────────────────────

    private sealed record ParsedFile(
        char Delimiter,
        IReadOnlyList<string> Columns,
        ImportSuggestedMappingDTO Suggested,
        List<(int LineNumber, string[] Cells)> DataRows,
        bool Truncated);

    private static async Task<ParsedFile> ReadAsync(
        Stream csv, ImportMappingDTO mapping, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);

        var delimiter = !string.IsNullOrEmpty(mapping.Delimiter)
            ? mapping.Delimiter[0]
            : CsvStatementParser.DetectDelimiter(content);

        var all = CsvStatementParser.SplitRows(content, delimiter);

        // Line numbers are the user's coordinate system: they will open this file
        // in a spreadsheet to find a problem row, so the number has to survive the
        // skipped preamble and the header.
        var numbered = all.Select((cells, index) => (LineNumber: index + 1, Cells: cells)).ToList();

        if (mapping.SkipRows > 0)
            numbered = numbered.Skip(mapping.SkipRows).ToList();

        string[] header = [];
        if (mapping.HasHeaderRow && numbered.Count > 0)
        {
            header = numbered[0].Cells;
            numbered = numbered.Skip(1).ToList();
        }

        var width = Math.Max(
            header.Length,
            numbered.Count > 0 ? numbered.Max(r => r.Cells.Length) : 0);

        var columns = Enumerable.Range(0, width)
            .Select(i => i < header.Length && !string.IsNullOrWhiteSpace(header[i])
                ? header[i].Trim()
                : $"Column {i + 1}")
            .ToList();

        var truncated = numbered.Count > MaxRows;
        if (truncated) numbered = numbered.Take(MaxRows).ToList();

        var (date, amount, description, counterparty) = CsvStatementParser.SuggestColumns(header);

        return new ParsedFile(
            delimiter,
            columns,
            new ImportSuggestedMappingDTO
            {
                DateColumn = date,
                AmountColumn = amount,
                DescriptionColumn = description,
                CounterpartyColumn = counterparty
            },
            numbered,
            truncated);
    }

    // ── Interpretation ─────────────────────────────────────────────────────

    private async Task<List<ImportPreviewRowDTO>> BuildRowsAsync(
        ParsedFile file, ImportMappingDTO mapping, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var accountExists = await _context.Accounts
            .AnyAsync(a => a.Id == mapping.AccountId && a.UserId == userId, cancellationToken);
        if (!accountExists)
            throw new InvalidOperationException("Account not found.");

        // Every category id in the mapping is checked against this user's own
        // categories — a rule is user-supplied and could otherwise point at
        // someone else's row (Fix-8's lesson).
        var categories = await _context.Categories
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.Name, c.CategoryType })
            .ToListAsync(cancellationToken);

        var byId = categories.ToDictionary(c => c.Id);

        if (!byId.TryGetValue(mapping.DefaultExpenseCategoryId, out var defaultExpense)
            || defaultExpense.CategoryType != CategoryType.Expense)
            throw new InvalidOperationException("Default expense category not found.");

        if (!byId.TryGetValue(mapping.DefaultIncomeCategoryId, out var defaultIncome)
            || defaultIncome.CategoryType != CategoryType.Income)
            throw new InvalidOperationException("Default income category not found.");

        var rules = mapping.Rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Contains) && byId.ContainsKey(r.CategoryId))
            .Select(r => (Needle: CsvStatementParser.NormalizeDescription(r.Contains), r.CategoryId))
            .Where(r => r.Needle.Length > 0)
            .ToList();

        var culture = ResolveCulture(mapping.Culture);
        var rows = new List<ImportPreviewRowDTO>(file.DataRows.Count);

        // First pass: read the cells. The duplicate check needs the date range, so
        // it cannot happen until every date is known.
        var parsed = new List<(int LineNumber, string[] Cells, DateOnly? Date, decimal Signed, string Title, string? Counterparty, string? Problem)>();

        foreach (var (lineNumber, cells) in file.DataRows)
        {
            string? problem = null;

            var dateText = Cell(cells, mapping.DateColumn);
            var amountText = Cell(cells, mapping.AmountColumn);
            var descriptionText = Cell(cells, mapping.DescriptionColumn);
            var counterpartyText = mapping.CounterpartyColumn is int cp ? Cell(cells, cp) : null;

            DateOnly? date = null;
            if (!CsvStatementParser.TryParseDate(dateText, culture, mapping.DateFormat, out var parsedDate))
                problem = string.IsNullOrWhiteSpace(dateText) ? "No date in this row." : $"Could not read the date \"{dateText}\".";
            else date = parsedDate;

            decimal signed = 0m;
            if (problem is null && !CsvStatementParser.TryParseAmount(amountText, culture, out signed))
                problem = string.IsNullOrWhiteSpace(amountText) ? "No amount in this row." : $"Could not read the amount \"{amountText}\".";

            if (problem is null && signed == 0m)
                problem = "The amount is zero.";

            var title = !string.IsNullOrWhiteSpace(descriptionText)
                ? descriptionText!.Trim()
                : counterpartyText?.Trim() ?? string.Empty;

            if (problem is null && title.Length == 0)
                problem = "This row has no description to name it by.";

            parsed.Add((lineNumber, cells, date, signed, title, counterpartyText?.Trim(), problem));
        }

        var seen = await LoadExistingKeysAsync(
            mapping.AccountId,
            parsed.Where(p => p.Date is not null).Select(p => p.Date!.Value).ToList(),
            cancellationToken);

        foreach (var row in parsed)
        {
            if (row.Problem is not null)
            {
                rows.Add(new ImportPreviewRowDTO
                {
                    RowNumber = row.LineNumber,
                    Raw = row.Cells,
                    Title = row.Title,
                    Problem = row.Problem
                });
                continue;
            }

            // NegativeIsExpense is the usual bank convention; flipping it covers
            // the exports that state a debit as a positive number in a column
            // called "Withdrawal".
            var isExpense = mapping.NegativeIsExpense ? row.Signed < 0m : row.Signed > 0m;
            var amount = Math.Abs(row.Signed);

            var haystack = CsvStatementParser.NormalizeDescription($"{row.Title} {row.Counterparty}");
            var ruleMatch = rules.FirstOrDefault(r => haystack.Contains(r.Needle, StringComparison.Ordinal));

            var categoryId = ruleMatch.CategoryId != Guid.Empty
                             && byId[ruleMatch.CategoryId].CategoryType == (isExpense ? CategoryType.Expense : CategoryType.Income)
                ? ruleMatch.CategoryId
                : isExpense ? mapping.DefaultExpenseCategoryId : mapping.DefaultIncomeCategoryId;

            // The hash Sugg-6 asked for, computed rather than stored: account is
            // fixed for the whole import, so (date, amount, normalized memo) is the
            // identity. `seen` starts as what is already in the database and grows
            // as the file is walked, so a statement that repeats a line inside
            // itself is caught too.
            var key = DuplicateKey(row.Date!.Value, isExpense, amount, row.Title);
            var isDuplicate = !seen.Add(key);

            rows.Add(new ImportPreviewRowDTO
            {
                RowNumber = row.LineNumber,
                Raw = row.Cells,
                Date = row.Date,
                Amount = amount,
                IsExpense = isExpense,
                Title = row.Title,
                Description = row.Counterparty,
                CategoryId = categoryId,
                CategoryName = byId[categoryId].Name,
                CategoryFromRule = categoryId == ruleMatch.CategoryId && ruleMatch.CategoryId != Guid.Empty,
                IsDuplicate = isDuplicate
            });
        }

        return rows;
    }

    /// <summary>
    /// Existing rows in this account that could collide, as duplicate keys.
    /// Bounded to the file's own date range so re-importing one month does not
    /// read a decade of history.
    /// </summary>
    private async Task<HashSet<string>> LoadExistingKeysAsync(
        Guid accountId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (dates.Count == 0) return keys;

        var userId = _currentUser.UserId;
        var from = dates.Min();
        var to = dates.Max();

        var expenses = await _context.Expenses
            .Where(e => e.UserId == userId && e.AccountId == accountId && e.Date >= from && e.Date <= to)
            .Select(e => new { e.Date, e.Amount, e.Title })
            .ToListAsync(cancellationToken);

        foreach (var e in expenses)
            keys.Add(DuplicateKey(e.Date, isExpense: true, e.Amount, e.Title));

        var incomes = await _context.Incomes
            .Where(i => i.UserId == userId && i.AccountId == accountId && i.Date >= from && i.Date <= to)
            .Select(i => new { i.Date, i.Amount, i.Description })
            .ToListAsync(cancellationToken);

        foreach (var i in incomes)
            keys.Add(DuplicateKey(i.Date, isExpense: false, i.Amount, i.Description));

        return keys;
    }

    private static string DuplicateKey(DateOnly date, bool isExpense, decimal amount, string? description)
    {
        var normalized = CsvStatementParser.NormalizeDescription(description);
        // An income row's description is built from the memo on import, so compare
        // only the leading part a re-import would reproduce identically.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{date:yyyy-MM-dd}|{(isExpense ? 'E' : 'I')}|{amount:0.##}|{normalized}");
    }

    private static string? Cell(string[] cells, int index) =>
        index >= 0 && index < cells.Length ? cells[index] : null;

    private static CultureInfo ResolveCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return CultureInfo.InvariantCulture;

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            // A profile carrying a culture this container's ICU does not have must
            // not fail the whole import — invariant reads ISO dates and dotted
            // decimals, which is the most common export shape anyway.
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>Null rather than "" for an absent optional field, so the column stays honest.</summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
