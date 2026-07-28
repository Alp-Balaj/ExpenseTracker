using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.DTOs.Reports;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Services
{
    public sealed class TransactionReportService : ITransactionReportService
    {
        private readonly IUnitOfWork _uow;
        private readonly ICurrencyConversionService _conversion;
        public TransactionReportService(IUnitOfWork uow, ICurrencyConversionService conversion)
        {
            _uow = uow;
            _conversion = conversion;
        }
        public async Task<TransactionReportResult> GenerateAsync(TransactionReportRequest request, CancellationToken cancellationToken = default)
        {
            Validate(request);

            var (from, toExclusive) = NormalizeRange(request.From, request.To);

            var categoryIds = request.CategoryIds is { Length: > 0 } ? request.CategoryIds : null;
            var accountIds = request.AccountIds is { Length: > 0 } ? request.AccountIds : null;

            var rows = new List<TransactionReportRow>();

            if (request.IncludeExpenses)
            {
                var q = _uow.Expenses.Query()
                    .Where(x => x.Date >= from && x.Date < toExclusive);

                if (categoryIds != null)
                    q = q.Where(x => categoryIds.Contains(x.CategoryId));

                if (accountIds != null)
                    q = q.Where(x => accountIds.Contains(x.AccountId));

                var expenseRows = await q
                    .Select(x => new TransactionReportRow
                    {
                        Date = x.Date,
                        Kind = TransactionKind.Expense,
                        Category = x.Category.Name,
                        Account = x.Account.Name,
                        Amount = -x.Amount,
                        Currency = x.Account.Currency.Code
                    })
                    .ToListAsync(cancellationToken);

                rows.AddRange(expenseRows);
            }

            if (request.IncludeIncomes)
            {
                var q = _uow.Incomes.Query()
                    .Where(x => x.Date >= from && x.Date < toExclusive);

                if (categoryIds != null)
                    q = q.Where(x => categoryIds.Contains(x.CategoryId));

                if (accountIds != null)
                    q = q.Where(x => accountIds.Contains(x.AccountId));

                var incomeRows = await q
                    .Select(x => new TransactionReportRow
                    {
                        Date = x.Date,
                        Kind = TransactionKind.Income,
                        Category = x.Category.Name,
                        Account = x.Account.Name,
                        Amount = x.Amount,
                        Currency = x.Account.Currency.Code
                    })
                    .ToListAsync(cancellationToken);

                rows.AddRange(incomeRows);
            }

            rows = rows.OrderBy(r => r.Date).ToList();

            var converter = await _conversion.GetConverterAsync(cancellationToken);
            var breakdown = converter.Summarize(
                rows.Select(r => new CurrencyAmount(r.Currency, r.Amount)));

            return new TransactionReportResult
            {
                Rows = rows,
                Total = rows.Sum(r => r.Amount),
                Subtotals = breakdown.Subtotals,
                BaseCurrency = breakdown.BaseCurrency,
                ConvertedTotal = breakdown.ConvertedTotal,
                HasMissingRates = breakdown.HasMissingRates
            };
        }

        private static void Validate(TransactionReportRequest request)
        {
            if (request.To < request.From)
                throw new ArgumentException("To date must be >= From date.");

            if (!request.IncludeExpenses && !request.IncludeIncomes)
                throw new ArgumentException(
                    "At least one of IncludeExpenses or IncludeIncomes must be true.");
        }
        private static (DateOnly from, DateOnly toExclusive) NormalizeRange(DateOnly from, DateOnly to)
        {
            return (from, to.AddDays(1));
        }
    }
}
