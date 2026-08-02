namespace ExpenseTracker.Application.Interfaces
{
    /// <summary>
    /// Seeds the starter catalogue (currencies, categories, one account, preferences)
    /// for a user that has no data yet. Called by signup inside the same transaction
    /// that creates the Identity user; deliberately standalone and parameterisable so
    /// other flows (demo-data fabrication, admin repair) can reuse it for any user id.
    /// </summary>
    public interface IUserDataSeeder
    {
        /// <summary>
        /// Creates the starter catalogue for <paramref name="userId"/> with
        /// <paramref name="baseCurrencyCode"/> as the base currency (ISO 4217, e.g. "EUR").
        /// No-ops — returning <c>false</c> — when the user already owns any currency,
        /// category or account, so it can never duplicate or touch existing data.
        /// </summary>
        /// <returns><c>true</c> when the catalogue was written, <c>false</c> when skipped.</returns>
        Task<bool> SeedNewUserAsync(string userId, string baseCurrencyCode = "EUR", CancellationToken cancellationToken = default);
    }
}
