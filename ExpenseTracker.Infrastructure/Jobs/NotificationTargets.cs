using System.Globalization;
using ExpenseTracker.Application.Services;
using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Jobs;

/// <summary>
/// The two things all three notification jobs need before they can evaluate
/// anything: who to evaluate, and what their money is denominated in.
/// </summary>
internal static class NotificationTargets
{
    /// <summary>
    /// Users with at least one registered device — the only users any of this is
    /// worth computing for. Someone who never granted the permission should cost
    /// the nightly sweep exactly one join.
    /// </summary>
    public static Task<List<string>> WithSubscriptionsAsync(
        ApplicationDbContext context, CancellationToken cancellationToken) =>
        context.PushSubscriptions
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Builds the same rate snapshot <see cref="CurrencyConversionService"/>
    /// builds for an HTTP request, but against an explicit user — the jobs run
    /// with no HTTP context, so the current-user services are unavailable to them.
    /// <para>
    /// Sharing <see cref="CurrencyConverter"/> rather than re-implementing the
    /// arithmetic is the point: a notification that disagrees with the number on
    /// the dashboard is worse than no notification.
    /// </para>
    /// </summary>
    public static async Task<CurrencyConverter> ConverterForAsync(
        ApplicationDbContext context, string userId, CancellationToken cancellationToken)
    {
        var baseCurrency = await context.UserPreferences
            .Where(p => p.UserId == userId)
            .Select(p => p.BaseCurrency)
            .FirstOrDefaultAsync(cancellationToken);

        var rates = await context.Currencies
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Code, c.ExchangeRateToBase })
            .ToListAsync(cancellationToken);

        var lookup = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var rate in rates)
            lookup[rate.Code] = rate.ExchangeRateToBase;

        return new CurrencyConverter(
            string.IsNullOrWhiteSpace(baseCurrency) ? null : baseCurrency.Trim(),
            lookup);
    }

    /// <summary>
    /// Money as it appears in a notification body. The ISO code rather than a
    /// symbol: the notification tray has no idea what locale the app is in, and
    /// "1,240.00 EUR" is never ambiguous where "$1,240.00" can be.
    /// </summary>
    public static string Money(decimal amount, string? currencyCode)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var formatted = rounded.ToString("N2", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(currencyCode) ? formatted : $"{formatted} {currencyCode}";
    }
}
