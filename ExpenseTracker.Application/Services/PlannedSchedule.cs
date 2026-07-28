using System.Globalization;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Shared.Enums;

namespace ExpenseTracker.Application.Services;

/// <summary>
/// When a planned transaction comes round, and what to call each occurrence.
///
/// Pure: no database, no clock of its own — "today" is always passed in, so the
/// awkward cases (a rent anchored to the 31st, a yearly on 29 February, a
/// template backdated by a year) are unit-testable without a container.
/// </summary>
public static class PlannedSchedule
{
    /// <summary>
    /// The day "due" is measured against, defined once so the generator, the
    /// look-ahead and the controller can never disagree.
    /// <para>
    /// UTC, not server-local: the app stores no per-user timezone, so there is no
    /// "the user's today" to be had, and <c>DateTime.Now</c> would only make the
    /// boundary depend on where the container runs. The cost is that a bill can
    /// surface up to a day either side of local midnight — which for a queue the
    /// user confirms by hand is not a cost at all.
    /// </para>
    /// </summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// The period a due date belongs to, in a form two independent runs of the
    /// generator are guaranteed to agree on. Paired with the template id this is
    /// the unique key that stops a period being queued twice.
    /// </summary>
    public static string PeriodKeyFor(Recurrence recurrence, DateOnly dueDate) => recurrence switch
    {
        // A one-off has exactly one period, so it needs no discriminator — and
        // using the date would let an edit to StartDate queue a second one.
        Recurrence.Once => "once",
        // ISO weeks, so the turn of the year cannot produce two occurrences that
        // claim the same key: 29 December 2025 is 2026-W01, not 2025-W53.
        Recurrence.Weekly => IsoWeekKey(dueDate),
        Recurrence.Monthly => dueDate.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        Recurrence.Yearly => dueDate.ToString("yyyy", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(recurrence), recurrence, "Unknown recurrence.")
    };

    private static string IsoWeekKey(DateOnly dueDate)
    {
        var asDateTime = dueDate.ToDateTime(TimeOnly.MinValue);
        var year = ISOWeek.GetYear(asDateTime).ToString(CultureInfo.InvariantCulture);
        var week = ISOWeek.GetWeekOfYear(asDateTime).ToString("00", CultureInfo.InvariantCulture);
        return $"{year}-W{week}";
    }

    /// <summary>
    /// How far back a first sweep will reach. A template backdated years ("this
    /// rent has been running since 2019") means the schedule is historical, not
    /// that the user wants seventy reminders to confirm one by one — so anything
    /// older than this is treated as already dealt with.
    /// </summary>
    public const int MaxLookbackDays = 366;

    /// <summary>
    /// Hard ceiling on one template's output from a single sweep. Only reachable
    /// via <see cref="MaxLookbackDays"/> of weekly occurrences plus the horizon;
    /// it exists so a future recurrence with a bad anchor can never spin.
    /// </summary>
    private const int MaxOccurrencesPerSweep = 500;

    /// <summary>
    /// Every occurrence of <paramref name="template"/> that has come due on or
    /// before <paramref name="today"/>, oldest first.
    /// </summary>
    /// <remarks>
    /// Bounded at both ends: never earlier than <see cref="MaxLookbackDays"/>
    /// before today, never later than today (nothing is queued before it is
    /// actually owed — the dashboard's look-ahead is computed, not stored), and
    /// never past the template's EndDate.
    /// </remarks>
    public static IEnumerable<(string PeriodKey, DateOnly DueDate)> DueOccurrences(
        PlannedTransaction template, DateOnly today)
    {
        var floor = today.AddDays(-MaxLookbackDays);
        var last = template.EndDate is DateOnly end && end < today ? end : today;

        var emitted = 0;
        foreach (var dueDate in DueDates(template, last))
        {
            if (++emitted > MaxOccurrencesPerSweep) yield break;
            if (dueDate < floor) continue;
            yield return (PeriodKeyFor(template.Recurrence, dueDate), dueDate);
        }
    }

    /// <summary>
    /// The raw schedule from StartDate to <paramref name="last"/> inclusive,
    /// before any lookback trimming.
    /// </summary>
    private static IEnumerable<DateOnly> DueDates(PlannedTransaction template, DateOnly last)
    {
        var start = template.StartDate;
        if (start > last) yield break;

        switch (template.Recurrence)
        {
            case Recurrence.Once:
                yield return start;
                break;

            case Recurrence.Weekly:
                for (var date = start; date <= last; date = date.AddDays(7))
                    yield return date;
                break;

            case Recurrence.Monthly:
                // Stepped by month index off the start, never by adding a month to
                // the previous *clamped* date: a rent anchored to the 31st has to
                // come back to the 31st in March after landing on the 28th in
                // February, and date.AddMonths(1) would have pinned it to the 28th
                // for the rest of its life.
                for (var step = 0; ; step++)
                {
                    var anchor = start.AddMonths(step);
                    var date = ClampToMonth(anchor.Year, anchor.Month, start.Day);
                    if (date > last) yield break;
                    yield return date;
                }

            case Recurrence.Yearly:
                // Same reasoning for 29 February, which only exists every fourth
                // year and must not be demoted to the 28th permanently.
                for (var step = 0; ; step++)
                {
                    var date = ClampToMonth(start.Year + step, start.Month, start.Day);
                    if (date > last) yield break;
                    yield return date;
                }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(template), template.Recurrence, "Unknown recurrence.");
        }
    }

    /// <summary>
    /// The anchor day within a month, pulled back to the last day when that month
    /// is too short — the 31st becomes the 30th in April and the 28th (or 29th)
    /// in February, which is what a bank does with a standing order.
    /// </summary>
    private static DateOnly ClampToMonth(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <summary>
    /// The next date this template comes due strictly after
    /// <paramref name="after"/>, or null once it is finished. Drives the "rent in
    /// 12 days" look-ahead, which is computed rather than queued.
    /// </summary>
    public static DateOnly? NextDueAfter(PlannedTransaction template, DateOnly after)
    {
        // A window wide enough for any supported recurrence to produce its next
        // date — a year and a day covers yearly, everything else lands sooner.
        var horizon = after.AddDays(367);
        if (template.EndDate is DateOnly end && end < horizon) horizon = end;

        foreach (var date in DueDates(template, horizon))
        {
            if (date > after) return date;
        }

        return null;
    }
}
