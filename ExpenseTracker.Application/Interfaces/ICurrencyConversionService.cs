using ExpenseTracker.Application.Services;

namespace ExpenseTracker.Application.Interfaces
{
    public interface ICurrencyConversionService
    {
        /// <summary>
        /// Loads the current user's base currency and exchange rates once and returns
        /// a snapshot that can summarize any number of amount sets without further I/O.
        /// </summary>
        Task<CurrencyConverter> GetConverterAsync();
    }
}
