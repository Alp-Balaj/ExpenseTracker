using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Shared.Enums;
namespace ExpenseTracker.Application.Mapping
{
    public static class AccountMapping
    {
        public static AccountDTO ToDto(this Account account)
        {
            return new AccountDTO
            {
                Id = account.Id,
                Name = account.Name,
                AmountTypeId = (int)account.AmountType,
                BalanceCurrencyId = account.BalanceCurrencyId,
            };
        }
        public static Account ToEntity(this AccountDTO accountDTO)
        {
            return new Account
            {
                Name = accountDTO.Name,
                AmountType = (AmountType)accountDTO.AmountTypeId,
                BalanceCurrencyId = accountDTO.BalanceCurrencyId,
            };
        }
        public static void Apply(this Account entity, AccountDTO dto)
        {
            entity.Name = dto.Name;
            entity.AmountType = (AmountType)dto.AmountTypeId;
            entity.BalanceCurrencyId = dto.BalanceCurrencyId;
        }
    }
}
