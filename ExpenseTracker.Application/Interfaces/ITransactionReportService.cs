using ExpenseTracker.Application.DTOs.Reports;

namespace ExpenseTracker.Application.Interfaces
{
    public interface ITransactionReportService
    {
        Task<TransactionReportResult> GenerateAsync(TransactionReportRequest request);
    }
}
