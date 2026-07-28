using ExpenseTracker.Application.DTOs;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Interfaces;

namespace ExpenseTracker.Application.Services
{
    public sealed class CurrencyConversionService : ICurrencyConversionService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IUserPreferencesService _preferences;

        public CurrencyConversionService(
            IUnitOfWork unitOfWork,
            IUserPreferencesService preferences)
        {
            _unitOfWork = unitOfWork;
            _preferences = preferences;
        }

        public async Task<CurrencyConverter> GetConverterAsync(CancellationToken cancellationToken = default)
        {
            string? baseCurrency;
            try
            {
                var prefs = await _preferences.GetAsync(cancellationToken);
                baseCurrency = string.IsNullOrWhiteSpace(prefs.BaseCurrency)
                    ? null
                    : prefs.BaseCurrency.Trim();
            }
            catch
            {
                // Preferences store unreachable: degrade to subtotals-only rather
                // than failing the whole report.
                baseCurrency = null;
            }

            var currencies = await _unitOfWork.Currencies.GetAllUserDataAsync(cancellationToken);

            var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var currency in currencies)
                rates[currency.Code] = currency.ExchangeRateToBase;

            return new CurrencyConverter(baseCurrency, rates);
        }
    }

    /// <summary>
    /// Immutable snapshot of the user's base currency and exchange rates.
    /// Rate direction: <c>amountInBase = amount * ExchangeRateToBase</c>
    /// (the rate maps that currency into the base currency).
    /// </summary>
    public sealed class CurrencyConverter
    {
        private readonly IReadOnlyDictionary<string, decimal> _rates;

        public CurrencyConverter(string? baseCurrency, IReadOnlyDictionary<string, decimal> rates)
        {
            BaseCurrency = baseCurrency;
            _rates = rates;
        }

        public string? BaseCurrency { get; }

        public CurrencyBreakdown Summarize(IEnumerable<CurrencyAmount> items)
        {
            var subtotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            decimal convertedTotal = 0m;
            var hasMissingRates = false;
            var hasItems = false;

            foreach (var item in items)
            {
                hasItems = true;

                // A null/empty currency means the amount carries no currency of its
                // own (e.g. future expenses) and is assumed to already be in base.
                var isBase = string.IsNullOrWhiteSpace(item.Currency)
                    || string.Equals(item.Currency, BaseCurrency, StringComparison.OrdinalIgnoreCase);

                var code = string.IsNullOrWhiteSpace(item.Currency)
                    ? BaseCurrency ?? string.Empty
                    : item.Currency;

                subtotals[code] = subtotals.GetValueOrDefault(code, 0m) + item.Amount;

                if (isBase)
                    convertedTotal += item.Amount;
                else if (_rates.TryGetValue(code, out var rate) && rate > 0m)
                    convertedTotal += item.Amount * rate;
                else
                    hasMissingRates = true;
            }

            // Without a base currency there is nothing meaningful to convert into.
            if (BaseCurrency is null && hasItems)
                hasMissingRates = true;

            return new CurrencyBreakdown
            {
                Subtotals = subtotals
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new CurrencySubtotalDTO { Currency = kv.Key, Total = kv.Value })
                    .ToList(),
                BaseCurrency = BaseCurrency,
                ConvertedTotal = hasMissingRates || BaseCurrency is null ? null : convertedTotal,
                HasMissingRates = hasMissingRates
            };
        }
    }
}
