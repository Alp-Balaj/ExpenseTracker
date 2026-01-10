using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Shared.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
namespace ExpenseTracker.Application.Mapping
{
    public static class AccountMapping
    {
        public static AccountDTO ToDto(this Account account)
        {
            return new AccountDTO
            {
                Name = account.Name,
                TypeId = (int)account.AmountType,
                BalanceCurrencyId = account.BalanceCurrencyId,
            };
        }
        public static Account ToEntity(this AccountDTO accountDTO)
        {
            return new Account
            {
                Name = accountDTO.Name,
                AmountType = (AmountType)accountDTO.TypeId,
                BalanceCurrencyId = accountDTO.BalanceCurrencyId,
            };
        }
        public static void Apply(this Account entity, AccountDTO dto)
        {
            entity.Name = dto.Name;
            entity.AmountType = (AmountType)dto.TypeId;
            entity.BalanceCurrencyId = dto.BalanceCurrencyId;
        }
    }
}
