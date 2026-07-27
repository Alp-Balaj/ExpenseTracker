using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Domain.Entities;
namespace ExpenseTracker.Application.Mapping
{
    public static class CurrencyMapping
    {
        public static CurrencyDTO ToDto(this Currency entity)
        {
            return new CurrencyDTO
            {
                Id = entity.Id,
                Name = entity.Name,
                Code = entity.Code,
                Symbol = entity.Symbol,
                ExchangeRateToBase = entity.ExchangeRateToBase,
                RateUpdatedAtUtc = entity.RateUpdatedAtUtc
            };
        }
        public static Currency ToEntity(this CurrencyDTO dto)
        {
            return new Currency
            {
                UserId = null!,

                Name = dto.Name,
                Code = dto.Code,
                Symbol = dto.Symbol,
                ExchangeRateToBase = dto.ExchangeRateToBase,
                // A hand-entered rate is exactly as fresh as the moment it was typed.
                // Stamping here (rather than trusting the DTO) keeps RateUpdatedAtUtc
                // server-owned, so a client can't backdate its own rates.
                RateUpdatedAtUtc = DateTime.UtcNow
            };
        }
        public static void Apply(this Currency entity, CurrencyDTO dto)
        {
            entity.Name = dto.Name;
            entity.Code = dto.Code;
            entity.Symbol = dto.Symbol;

            // Only a changed rate resets the age. Renaming a currency or fixing its
            // symbol must not make a months-old rate read as fetched today.
            if (entity.ExchangeRateToBase != dto.ExchangeRateToBase)
            {
                entity.ExchangeRateToBase = dto.ExchangeRateToBase;
                entity.RateUpdatedAtUtc = DateTime.UtcNow;
            }
        }
        public static CurrencyDropdownDTO ToDropdownDto(this Currency entity)
        {
            return new CurrencyDropdownDTO
            {
                Id = entity.Id,
                Code = entity.Code,
                Name = entity.Name,
                Symbol = entity.Symbol,
            };
        }
    }
}
