using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.DTOs.Reports;

public sealed class TransactionReportRow
{
    public DateOnly Date { get; init; }

    public TransactionKind Kind { get; init; }

    public string Category { get; init; } = default!;
    public string Account { get; init; } = default!;

    public decimal Amount { get; init; }
    public string Currency { get; init; } = default!;
}
