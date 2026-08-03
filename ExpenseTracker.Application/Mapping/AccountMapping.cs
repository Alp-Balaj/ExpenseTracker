using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Shared.Enums;
namespace ExpenseTracker.Application.Mapping
{
    public static class AccountMapping
    {
        /// <param name="reserved">
        /// What savings goals have spoken for on this account. Passed in rather
        /// than read off a navigation: it is one grouped query for the whole
        /// list, never a sum per row.
        /// </param>
        public static AccountDTO ToDto(this Account account, decimal reserved = 0m)
        {
            return new AccountDTO
            {
                Id = account.Id,
                Name = account.Name,
                AmountType = (int)account.AmountType,
                Balance = account.Balance,
                ReservedAmount = reserved,
                CurrencyId = account.CurrencyId,
                CurrencyCode = account.Currency?.Code
                    ?? throw new InvalidOperationException("Currency navigation not loaded on Account"),
                Description = account.Description,
            };
        }
        public static Account ToEntity(this AccountDTO accountDTO)
        {
            return new Account
            {
                UserId = null!,

                Name = accountDTO.Name,
                AmountType = (AmountType)accountDTO.AmountType,
                Balance = accountDTO.Balance,
                CurrencyId = accountDTO.CurrencyId,
                Description = accountDTO.Description,
            };
        }
        public static void Apply(this Account entity, AccountDTO dto)
        {
            entity.Name = dto.Name;
            entity.AmountType = (AmountType)dto.AmountType;
            // Balance is intentionally not applied: after creation it changes
            // only through expense/income transactions.
            entity.CurrencyId = dto.CurrencyId;
            entity.Description = dto.Description;
        }
    }
}
