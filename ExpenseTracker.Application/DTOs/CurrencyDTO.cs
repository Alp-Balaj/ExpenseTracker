namespace ExpenseTracker.Application.DTOs
{
    public class CurrencyDTO
    {
        public string Code { get; set; }
        public string Symbol { get; set; }
        public decimal ExchangeRateToBase { get; set; }
    }
}