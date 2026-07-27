using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Mapping
{
    public static class TransferMapping
    {
        public static TransferDTO ToDto(this Transfer entity)
        {
            return new TransferDTO
            {
                Id = entity.Id,
                Amount = entity.Amount,
                Date = entity.Date,
                Note = entity.Note,
                FromAccountId = entity.FromAccountId,
                ToAccountId = entity.ToAccountId,
                SavingGoalId = entity.SavingGoalId,
                SavingContributionId = entity.SavingContributionId
            };
        }

        // SavingContributionId is deliberately not mapped in either direction
        // below: the controller owns that link, and letting a client set it
        // would point a transfer at a contribution it never created.
        public static Transfer ToEntity(this TransferDTO dto)
        {
            return new Transfer
            {
                UserId = null!,

                Amount = dto.Amount,
                Date = dto.Date,
                Note = dto.Note,
                FromAccountId = dto.FromAccountId,
                ToAccountId = dto.ToAccountId,
                SavingGoalId = dto.SavingGoalId,

                FromAccount = null!,
                ToAccount = null!
            };
        }

        public static void Apply(this Transfer entity, TransferDTO dto)
        {
            entity.Amount = dto.Amount;
            entity.Date = dto.Date;
            entity.Note = dto.Note;
            entity.FromAccountId = dto.FromAccountId;
            entity.ToAccountId = dto.ToAccountId;
            entity.SavingGoalId = dto.SavingGoalId;
        }
    }
}
