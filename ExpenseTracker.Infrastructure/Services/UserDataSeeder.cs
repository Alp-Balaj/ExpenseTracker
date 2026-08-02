using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Entities.User;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Services;

/// <summary>
/// Writes the starter catalogue for a brand-new user.
///
/// Goes to <see cref="ApplicationDbContext"/> directly rather than through
/// <c>IUserRelatedRepository</c>/<c>IUnitOfWork</c> for the same reason
/// <see cref="ExchangeRateUpdater"/> does: those scope everything to
/// <c>ICurrentUserServices.UserId</c>, which does not exist during signup —
/// the request is anonymous, the user row is seconds old. The user id is
/// passed in explicitly and stamped on every row here instead.
///
/// The single <c>SaveChangesAsync</c> at the end enlists in whatever
/// transaction the caller has opened (signup wraps user creation + seeding
/// in one), so a failed seed rolls the whole registration back.
/// </summary>
public sealed class UserDataSeeder : IUserDataSeeder
{
    /// <summary>
    /// Codes every new user gets besides their base. Rates are static bootstrap
    /// values (anchored to the catalogue below) with <c>RateUpdatedAtUtc</c> left
    /// null — the honest "never fetched" state — so the daily exchange-rate sweep
    /// (Sugg-8) replaces them with real quotes within a day and stamps the age.
    /// </summary>
    private static readonly string[] CommonCurrencyCodes = { "EUR", "USD", "GBP" };

    /// <summary>Display name, symbol and an approximate rate into EUR per code.</summary>
    private static readonly Dictionary<string, (string Name, string Symbol, decimal RateToEur)> CurrencyCatalogue =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = ("Euro", "€", 1m),
            ["USD"] = ("US Dollar", "$", 0.88m),
            ["GBP"] = ("British Pound", "£", 1.17m),
        };

    private static readonly (string Name, string Color)[] ExpenseCategories =
    {
        ("Groceries", "#4CAF50"),
        ("Eating Out", "#FF7043"),
        ("Transport", "#42A5F5"),
        ("Housing", "#8D6E63"),
        ("Utilities", "#FFCA28"),
        ("Health", "#EF5350"),
        ("Entertainment", "#AB47BC"),
        ("Shopping", "#EC407A"),
        ("Other", "#90A4AE"),
    };

    private static readonly (string Name, string Color)[] IncomeCategories =
    {
        ("Salary", "#66BB6A"),
        ("Other", "#78909C"),
    };

    private const string DefaultAccountName = "Main";

    private readonly ApplicationDbContext _context;
    private readonly ILogger<UserDataSeeder> _logger;

    public UserDataSeeder(ApplicationDbContext context, ILogger<UserDataSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> SeedNewUserAsync(
        string userId, string baseCurrencyCode = "EUR", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        baseCurrencyCode = string.IsNullOrWhiteSpace(baseCurrencyCode)
            ? "EUR"
            : baseCurrencyCode.Trim().ToUpperInvariant();

        // Existing data means this is not a fresh user: touch nothing. The guard is
        // what makes the seeder safe to point at any user id, ever.
        var hasData =
            await _context.Currencies.AnyAsync(c => c.UserId == userId, cancellationToken) ||
            await _context.Categories.AnyAsync(c => c.UserId == userId, cancellationToken) ||
            await _context.Accounts.AnyAsync(a => a.UserId == userId, cancellationToken);

        if (hasData)
        {
            _logger.LogInformation("Seed skipped for user {UserId}: data already present", userId);
            return false;
        }

        // Base currency first, always exactly 1 against itself.
        var baseKnown = CurrencyCatalogue.TryGetValue(baseCurrencyCode, out var baseInfo);
        var baseCurrency = new Currency
        {
            UserId = userId,
            Code = baseCurrencyCode,
            Name = baseKnown ? baseInfo.Name : baseCurrencyCode,
            Symbol = baseKnown ? baseInfo.Symbol : baseCurrencyCode,
            ExchangeRateToBase = 1m,
            RateUpdatedAtUtc = null,
        };
        _context.Currencies.Add(baseCurrency);

        // The common set, minus the base itself. Cross rates are computed through the
        // EUR anchor when both sides are known; an exotic base falls back to 1 (the
        // daily sweep or a manual edit corrects it — a placeholder is better than no row).
        var baseToEur = baseKnown ? baseInfo.RateToEur : (decimal?)null;
        foreach (var code in CommonCurrencyCodes)
        {
            if (string.Equals(code, baseCurrencyCode, StringComparison.OrdinalIgnoreCase))
                continue;

            var info = CurrencyCatalogue[code];
            var rate = baseToEur is decimal b and > 0m
                ? Math.Round(info.RateToEur / b, 6)
                : 1m;

            _context.Currencies.Add(new Currency
            {
                UserId = userId,
                Code = code,
                Name = info.Name,
                Symbol = info.Symbol,
                ExchangeRateToBase = rate,
                RateUpdatedAtUtc = null,
            });
        }

        foreach (var (name, color) in ExpenseCategories)
            _context.Categories.Add(new Category
            {
                UserId = userId,
                Name = name,
                CategoryType = CategoryType.Expense,
                Color = color,
            });

        foreach (var (name, color) in IncomeCategories)
            _context.Categories.Add(new Category
            {
                UserId = userId,
                Name = name,
                CategoryType = CategoryType.Income,
                Color = color,
            });

        _context.Accounts.Add(new Account
        {
            UserId = userId,
            Name = DefaultAccountName,
            AmountType = AmountType.CheckingAccount,
            Balance = 0m,
            CurrencyId = baseCurrency.Id,
            Currency = baseCurrency,
        });

        // Preferences row so the base currency choice is recorded where the rate
        // sweep and the SPA both read it. Guarded separately: a prefs row can exist
        // without any catalogue data (the PUT endpoint creates one on first save).
        if (!await _context.UserPreferences.AnyAsync(p => p.UserId == userId, cancellationToken))
            _context.UserPreferences.Add(new UserPreferences
            {
                UserId = userId,
                Theme = "system",
                BaseCurrency = baseCurrencyCode,
            });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded starter catalogue for user {UserId}: base {BaseCurrency}, {CurrencyCount} currencies, {CategoryCount} categories, 1 account",
            userId, baseCurrencyCode,
            CommonCurrencyCodes.Contains(baseCurrencyCode, StringComparer.OrdinalIgnoreCase) ? CommonCurrencyCodes.Length : CommonCurrencyCodes.Length + 1,
            ExpenseCategories.Length + IncomeCategories.Length);

        return true;
    }
}
