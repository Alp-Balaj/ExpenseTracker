namespace ExpenseTracker.Application.DTOs.Reports;

public sealed class TransactionReportResult
{
    public IReadOnlyList<TransactionReportRow> Rows { get; init; } = [];
    public decimal Total { get; init; }
}
