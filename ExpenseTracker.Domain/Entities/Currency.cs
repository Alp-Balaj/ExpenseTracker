using System.Diagnostics.CodeAnalysis;
using ExpenseTracker.Domain.Entities.Common;

namespace ExpenseTracker.Domain.Entities
{
    public class Currency : BaseEntity
    {
        [SetsRequiredMembers]
        public Currency() : base()
        {
            UserId = null!;
            Name = null!;
            Code = null!;
            Symbol = null!;
            ExchangeRateToBase = default!;
        }
        public required string Name { get; set; }
        public required string Code { get; set; }
        public required string Symbol { get; set; }

        /// <summary>
        /// Multiplier into the user's base currency:
        /// <c>amountInBase = amount * ExchangeRateToBase</c> (Fix-10's convention).
        /// The base currency's own row is always exactly 1.
        /// </summary>
        public required decimal ExchangeRateToBase { get; set; }

        /// <summary>
        /// When <see cref="ExchangeRateToBase"/> was last given a value — by the daily
        /// fetcher (Sugg-8) or by hand in the currency form. Null means "never recorded":
        /// rows created before Sugg-8, whose rate age is genuinely unknown. Drives the
        /// "rates from N days ago" hint, and is the reason a failed fetch is harmless —
        /// the old rate survives with its own, honest, timestamp.
        /// </summary>
        public DateTime? RateUpdatedAtUtc { get; set; }
    }
}