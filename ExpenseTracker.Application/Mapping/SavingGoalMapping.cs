using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
namespace ExpenseTracker.Application.Mapping
{
    public static class SavingGoalMapping
    {
        /// <param name="withContributions">
        /// Set only where the navigation was actually loaded. A goal read back
        /// after a write has none loaded, and an empty list there would read as
        /// "this goal has no history" rather than "nobody asked for it".
        /// </param>
        public static SavingGoalDTO ToDto(this SavingGoal entity, bool withContributions = false)
        {
            var dto = new SavingGoalDTO
            {
               Id = entity.Id,
               Title = entity.Title,
               TargetAmount = entity.TargetAmount,
               SavedAmount = entity.SavedAmount,
               Deadline = entity.Deadline,
               Description = entity.Description,
               CategoryId = entity.CategoryId,
               AccountId = entity.AccountId,
            };

            if (!withContributions) return dto;

            dto.Contributions = entity.SavingContributions
                .OrderByDescending(c => c.Date).ThenByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
                .Select(c => c.ToDto())
                .ToList();

            // The goal's currency is its earliest contribution's account's —
            // the same rule ISavingsReservationService enforces on write, read
            // off the rows already in hand rather than queried for again.
            dto.CurrencyCode = entity.SavingContributions
                .Where(c => c.Account?.Currency != null)
                .OrderBy(c => c.Date).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .Select(c => c.Account!.Currency.Code)
                .FirstOrDefault();

            return dto;
        }
        public static SavingGoal ToEntity(this SavingGoalDTO dto)
        {
            return new SavingGoal
            {
                UserId = null!,

                Title = dto.Title,
                TargetAmount = dto.TargetAmount,
                // SavedAmount is server-maintained: a new goal starts at 0 and
                // changes only through contributions.
                SavedAmount = 0,
                Deadline = dto.Deadline,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                AccountId = dto.AccountId,
            };
        }
        public static void Apply(this SavingGoal entity, SavingGoalDTO dto)
        {
            entity.Title = dto.Title;
            entity.TargetAmount = dto.TargetAmount;
            // SavedAmount is intentionally not applied: it changes only
            // through contributions.
            entity.Deadline = dto.Deadline;
            entity.Description = dto.Description;
            entity.CategoryId = dto.CategoryId;
            entity.AccountId = dto.AccountId;
        }
    }
}
