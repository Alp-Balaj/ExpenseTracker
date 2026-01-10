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
                Code = entity.Code,
                Symbol = entity.Symbol,
                ExchangeRateToBase = entity.ExchangeRateToBase
            };
        }
        public static Currency ToEntity(this CurrencyDTO dto)
        {
            return new Currency
            {
                Code = dto.Code,
                Symbol = dto.Symbol,
                ExchangeRateToBase = dto.ExchangeRateToBase
            };
        }
        public static void Apply(this Currency entity, CurrencyDTO dto)
        {
            entity.Code = dto.Code;
            entity.Symbol = dto.Symbol;
            entity.ExchangeRateToBase = dto.ExchangeRateToBase;
        }
    }
}
