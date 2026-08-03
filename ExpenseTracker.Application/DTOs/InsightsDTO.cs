namespace ExpenseTracker.Application.DTOs;

/// <summary>
/// How the current month's spending compares with recent history — the raw
/// numbers behind the dashboard's insight sentences. The API returns structure,
/// not prose: the frontend owns the wording.
/// </summary>
public sealed class InsightsDTO
{
    /// <summary>The month the insights were computed for, as yyyy-MM.</summary>
    public required string Month { get; init; }

    public string? BaseCurrency { get; init; }

    /// <summary>
    /// True when at least one expense could not be converted into the base
    /// currency; every converted figure then understates the real spend.
    /// </summary>
    public bool HasMissingRates { get; init; }

    /// <summary>
    /// Of the three months before the current one, how many contain at least one
    /// expense. Below three the averages are thin and the frontend says so
    /// instead of pretending a fortnight of data is "your usual".
    /// </summary>
    public int MonthsWithHistory { get; init; }

    /// <summary>Spend so far this month, converted into <see cref="BaseCurrency"/>.</summary>
    public decimal CurrentMonthTotal { get; init; }

    /// <summary>Last month's full spend, converted.</summary>
    public decimal PreviousMonthTotal { get; init; }

    /// <summary>
    /// What the user put up for other people on this month's split bills — the
    /// sum of every expected reimbursement dated in the current month, converted.
    /// <para>
    /// Deliberately gross of settlements: this is "how much of the month's
    /// spending was not really mine", and a friend paying up does not change how
    /// much was fronted in the first place.
    /// </para>
    /// </summary>
    public decimal FrontedThisMonth { get; init; }

    /// <summary>
    /// Everything still owed to the user across all of history (expected minus
    /// settled, converted). Paired with <see cref="FrontedThisMonth"/> this is
    /// the fronted-delta sentence: "you fronted X this month; Y is still out".
    /// Not scoped to the month — money out since spring is the money worth
    /// naming.
    /// </summary>
    public decimal StillOpen { get; init; }

    /// <summary>
    /// Average monthly spend over the previous three months, averaged over the
    /// months that actually have data. Null when there is no history at all.
    /// </summary>
    public decimal? TrailingAverage { get; init; }

    /// <summary>
    /// Current month vs <see cref="TrailingAverage"/>, as a percentage
    /// (positive = above the usual). Null on a cold start or a zero average.
    /// </summary>
    public decimal? OverallVsAveragePct { get; init; }

    /// <summary>
    /// Every expense category active in the window, biggest month-over-month
    /// movers first — the order the dashboard reads sentences off the top of.
    /// </summary>
    public IReadOnlyList<CategoryInsightDTO> Categories { get; init; } = [];
}

/// <summary>One category's spend measured against its own recent history.</summary>
public sealed class CategoryInsightDTO
{
    public Guid CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public string? CategoryColor { get; init; }

    /// <summary>Spend so far this month, in the base currency.</summary>
    public decimal CurrentMonthTotal { get; init; }

    /// <summary>Last month's spend, in the base currency.</summary>
    public decimal PreviousMonthTotal { get; init; }

    /// <summary>Current minus previous month — the size of the move.</summary>
    public decimal MomChangeAmount { get; init; }

    /// <summary>
    /// Month-over-month change as a percentage. Null when last month was zero,
    /// where any ratio would be nonsense.
    /// </summary>
    public decimal? MomChangePct { get; init; }

    /// <summary>
    /// This category's average over the previous three months (same denominator
    /// as the overall average). Null when there is no history.
    /// </summary>
    public decimal? TrailingAverage { get; init; }

    /// <summary>
    /// Current month vs the category's trailing average, as a percentage.
    /// Null on a cold start or when the average is zero.
    /// </summary>
    public decimal? VsAveragePct { get; init; }
}
