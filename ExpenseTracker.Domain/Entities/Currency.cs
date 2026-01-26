using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class Currency : BaseEntity
    {
        public string Name { get; set; }
        public string Code { get; set; }             
        public string Symbol { get; set; }
        public decimal ExchangeRateToBase { get; set; }
    }
}