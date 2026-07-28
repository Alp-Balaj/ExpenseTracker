using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Mapping
{
    public static class PlannedTransactionMapping
    {
        /// <param name="today">
        /// Passed in rather than read from a clock so the projection stays pure
        /// and the next-due figure agrees with whatever the caller generated
        /// against.
        /// </param>
        /// <param name="pendingCount">Occurrences of this template still waiting.</param>
        public static PlannedTransactionDTO ToDto(this PlannedTransaction entity, DateOnly today, int pendingCount = 0)
        {
            return new PlannedTransactionDTO
            {
                Id = entity.Id,
                Title = entity.Title,
                Amount = entity.Amount,
                Description = entity.Description,
                Kind = entity.Kind,
                CategoryId = entity.CategoryId,
                FromAccountId = entity.FromAccountId,
                ToAccountId = entity.ToAccountId,
                SavingGoalId = entity.SavingGoalId,
                Recurrence = entity.Recurrence,
                StartDate = entity.StartDate,
                EndDate = entity.EndDate,
                IsActive = entity.IsActive,
                // Computed, never stored: a paused template has no next date, and
                // "what is coming" is always derived from the schedule so the two
                // can never drift apart.
                NextDueDate = entity.IsActive ? PlannedSchedule.NextDueAfter(entity, today) : null,
                PendingCount = pendingCount
            };
        }

        public static PlannedTransaction ToEntity(this PlannedTransactionDTO dto)
        {
            return new PlannedTransaction
            {
                UserId = null!,

                Title = dto.Title,
                Amount = dto.Amount,
                Description = dto.Description,
                Kind = dto.Kind,
                CategoryId = dto.CategoryId,
                FromAccountId = dto.FromAccountId,
                ToAccountId = dto.ToAccountId,
                SavingGoalId = dto.SavingGoalId,
                Recurrence = dto.Recurrence,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                IsActive = dto.IsActive
            };
        }

        public static void Apply(this PlannedTransaction entity, PlannedTransactionDTO dto)
        {
            entity.Title = dto.Title;
            entity.Amount = dto.Amount;
            entity.Description = dto.Description;
            entity.Kind = dto.Kind;
            entity.CategoryId = dto.CategoryId;
            entity.FromAccountId = dto.FromAccountId;
            entity.ToAccountId = dto.ToAccountId;
            entity.SavingGoalId = dto.SavingGoalId;
            entity.Recurrence = dto.Recurrence;
            entity.StartDate = dto.StartDate;
            entity.EndDate = dto.EndDate;
            entity.IsActive = dto.IsActive;
        }

        /// <summary>
        /// Flattens an occurrence with its template, so the due queue is one
        /// request and the client never has to join the two itself.
        /// </summary>
        public static PlannedOccurrenceDTO ToDto(this PlannedOccurrence entity, PlannedTransaction template)
        {
            return new PlannedOccurrenceDTO
            {
                Id = entity.Id,
                PlannedTransactionId = entity.PlannedTransactionId,
                PeriodKey = entity.PeriodKey,
                DueDate = entity.DueDate,
                State = entity.State,
                CommittedAt = entity.CommittedAt,
                ExpenseId = entity.ExpenseId,
                IncomeId = entity.IncomeId,
                TransferId = entity.TransferId,

                Title = template.Title,
                Description = template.Description,
                Kind = template.Kind,
                Amount = template.Amount,
                CategoryId = template.CategoryId,
                FromAccountId = template.FromAccountId,
                ToAccountId = template.ToAccountId,
                SavingGoalId = template.SavingGoalId
            };
        }
    }
}
