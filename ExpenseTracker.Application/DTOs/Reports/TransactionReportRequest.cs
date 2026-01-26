namespace ExpenseTracker.Application.DTOs.Reports;

public sealed class TransactionReportRequest
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }

    public Guid[]? CategoryIds { get; init; }
    public Guid[]? AccountIds { get; init; }

    public bool IncludeExpenses { get; init; } = true;
    public bool IncludeIncomes { get; init; } = true;
}
