namespace ExpenseTracker.Application.DTOs
{
    public class CurrencyDTO
    {
        public Guid? Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string Symbol { get; set; }
        public decimal ExchangeRateToBase { get; set; }
    }

    public class CurrencyDropdownDTO
    {
        public Guid? Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string Symbol { get; set; }
    }
}