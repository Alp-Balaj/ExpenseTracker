using ExpenseTracker.Domain.Entities.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExpenseTracker.Domain.Entities
{
    public class Currency : BaseEntity
    {
        public string Code { get; set; }             
        public string Symbol { get; set; }
        public decimal ExchangeRateToBase { get; set; }
    }
}
