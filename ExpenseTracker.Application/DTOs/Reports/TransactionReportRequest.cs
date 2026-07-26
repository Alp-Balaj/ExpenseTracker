using ExpenseTracker.Application.Validation;

namespace ExpenseTracker.Application.DTOs.Reports;

public sealed class TransactionReportRequest
{
    [NotDefault]
    public DateTime From { get; init; }

    [NotDefault]
    public DateTime To { get; init; }

    public Guid[]? CategoryIds { get; init; }
    public Guid[]? AccountIds { get; init; }

    public bool IncludeExpenses { get; init; } = true;
    public bool IncludeIncomes { get; init; } = true;
}
