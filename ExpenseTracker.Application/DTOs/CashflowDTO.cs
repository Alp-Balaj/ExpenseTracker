namespace ExpenseTracker.Application.DTOs;

/// <summary>
/// A day-by-day projection of the liquid balance over the next month: today's
/// spendable money, minus the everyday burn rate, minus/plus every planned
/// bill and income as it falls due.
/// </summary>
public sealed class CashflowProjectionDTO
{
    /// <summary>The month the projection starts in, as yyyy-MM.</summary>
    public required string Month { get; init; }

    public string? BaseCurrency { get; init; }

    /// <summary>
    /// True when an account balance or a planned amount could not be converted
    /// into the base currency and was left out of the projection.
    /// </summary>
    public bool HasMissingRates { get; init; }

    /// <summary>The day the projection starts — the first point's date.</summary>
    public DateOnly Today { get; init; }

    /// <summary>The last day of the current month.</summary>
    public DateOnly EndOfMonth { get; init; }

    /// <summary>
    /// Liquid balance right now — checking, savings and cash accounts converted
    /// into the base currency. Credit and investments are not spendable.
    /// </summary>
    public decimal StartingBalance { get; init; }

    /// <summary>
    /// Estimated everyday spend per day: this month's expenses so far, excluding
    /// the ones posted from planned occurrences (those recur on their own
    /// schedule and are projected separately), divided by the days elapsed.
    /// </summary>
    public decimal DailyBurnRate { get; init; }

    /// <summary>The projected balance on <see cref="EndOfMonth"/>.</summary>
    public decimal ProjectedEndOfMonthBalance { get; init; }

    /// <summary>The projected balance at the end of the 30-day window.</summary>
    public decimal ProjectedEndBalance { get; init; }

    /// <summary>One point per day, today through today + 30, inclusive.</summary>
    public IReadOnlyList<CashflowPointDTO> Points { get; init; } = [];
}

/// <summary>One day of the projection.</summary>
public sealed class CashflowPointDTO
{
    public DateOnly Date { get; init; }

    /// <summary>The liquid balance projected for the end of this day.</summary>
    public decimal ProjectedBalance { get; init; }

    /// <summary>Planned bills applied on this day, positive = money out.</summary>
    public decimal PlannedOutflow { get; init; }

    /// <summary>Planned income applied on this day.</summary>
    public decimal PlannedInflow { get; init; }
}
