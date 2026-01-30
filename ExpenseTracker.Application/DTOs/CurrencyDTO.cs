namespace ExpenseTracker.Application.DTOs
{
    public class CurrencyDTO
    {
        public Guid? Id { get; set; }
        public required string Name { get; set; }
        public required string Code { get; set; }
        public required string Symbol { get; set; }
        public decimal ExchangeRateToBase { get; set; }
    }

    public class CurrencyDropdownDTO
    {
        public Guid? Id { get; set; }
        public required string Name { get; set; }
        public required string Code { get; set; }
        public required string Symbol { get; set; }
    }
}