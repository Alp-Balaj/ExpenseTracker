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
                ExchangeRateToBase = entity.ExchangeRateToBase
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
                ExchangeRateToBase = dto.ExchangeRateToBase
            };
        }
        public static void Apply(this Currency entity, CurrencyDTO dto)
        {
            entity.Name = dto.Name;
            entity.Code = dto.Code;
            entity.Symbol = dto.Symbol;
            entity.ExchangeRateToBase = dto.ExchangeRateToBase;
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
