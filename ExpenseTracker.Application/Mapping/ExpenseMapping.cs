using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Shared.Enums;
namespace ExpenseTracker.Application.Mapping
{
    public static class ExpenseMapping
    {
        /// <summary>
        /// Reads the reimbursement figures off the navigation, so a caller that
        /// has the graph in hand (the create path, which has just built it) gets
        /// them for free and one that has not sees an ordinary, unsplit expense.
        /// </summary>
        public static ExpenseDTO ToDto(this Expense entity) =>
            entity.ToDto(
                entity.Reimbursement?.ExpectedAmount,
                entity.Reimbursement?.Settlements.Sum(s => s.Amount) ?? 0m);

        /// <summary>
        /// The read-path overload: the list and detail queries compute the two
        /// server-owned figures in SQL rather than loading the settlement rows,
        /// and hand them in here.
        /// </summary>
        public static ExpenseDTO ToDto(this Expense entity, decimal? expectedReimbursement, decimal settledReimbursement)
        {
            return new ExpenseDTO
            {
                Id = entity.Id,
                Title = entity.Title,
                Amount = entity.Amount,
                Date = entity.Date,
                Description = entity.Description,
                AccountId = entity.AccountId,
                CategoryId = entity.CategoryId,

                ExpectedReimbursement = expectedReimbursement,
                SettledReimbursement = settledReimbursement,
                OpenReimbursement = (expectedReimbursement ?? 0m) - settledReimbursement
            };
        }
        public static Expense ToEntity(this ExpenseDTO dto)
        {
            return new Expense
            {
                UserId = null!,

                Title = dto.Title,
                Amount = dto.Amount,
                Date = dto.Date,
                Description = dto.Description,
                AccountId = dto.AccountId,
                CategoryId = dto.CategoryId,

                Account = null!,
                Category = null!
            };
        }
        public static void Apply(this Expense entity, ExpenseDTO dto)
        {
            entity.Title = dto.Title;
            entity.Amount = dto.Amount;
            entity.Date = dto.Date;
            entity.Description = dto.Description;
            entity.AccountId = dto.AccountId;
            entity.CategoryId = dto.CategoryId;
            // ExpectedReimbursement is deliberately absent from both ToEntity and
            // Apply: it lives in its own row, and moving it has to happen inside
            // the write path's transaction, where the settled total is known.
            // SettledReimbursement/OpenReimbursement are never applied at all —
            // server-owned, exactly like Account.Balance.
        }
    }
}
