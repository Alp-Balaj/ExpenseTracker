using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Mapping
{
    public static class ReimbursementMapping
    {
        /// <summary>
        /// The projection every reimbursement read goes through — expressed as an
        /// expression, not a method over materialised entities, so the settled
        /// total is a SQL aggregate rather than a loaded collection.
        /// <para>
        /// <see cref="ReimbursementDTO.OpenAmount"/> and
        /// <see cref="ReimbursementDTO.DaysOpen"/> are filled by
        /// <see cref="Complete"/> afterwards: one needs no database and the other
        /// needs today's date, which stays a parameter so the projection is pure.
        /// </para>
        /// </summary>
        public static IQueryable<ReimbursementDTO> ProjectToDto(this IQueryable<Reimbursement> query) =>
            query.Select(r => new ReimbursementDTO
            {
                Id = r.Id,
                ExpenseId = r.ExpenseId,
                ExpenseTitle = r.Expense.Title,
                ExpenseDate = r.Expense.Date,
                ExpenseAmount = r.Expense.Amount,
                ExpectedAmount = r.ExpectedAmount,
                SettledAmount = r.Settlements.Sum(s => (decimal?)s.Amount) ?? 0m,
                CurrencyCode = r.Expense.Account.Currency.Code,
                Settlements = r.Settlements
                    .OrderBy(s => s.Date)
                    .ThenBy(s => s.Id)
                    .Select(s => new ReimbursementSettlementDTO
                    {
                        Id = s.Id,
                        AccountId = s.AccountId,
                        AccountName = s.Account.Name,
                        Amount = s.Amount,
                        Date = s.Date
                    })
                    .ToList()
            });

        /// <param name="today">
        /// Passed in rather than read from a clock so the age agrees with
        /// whatever the caller measured everything else against.
        /// </param>
        public static ReimbursementDTO Complete(this ReimbursementDTO dto, DateOnly today)
        {
            dto.OpenAmount = dto.ExpectedAmount - dto.SettledAmount;
            // A future-dated expense is not owed to you from the future.
            dto.DaysOpen = Math.Max(0, today.DayNumber - dto.ExpenseDate.DayNumber);
            return dto;
        }
    }
}
