using ExpenseTracker.Application.Services;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Shared.Enums;
using Xunit;

namespace ExpenseTracker.Tests.UnitTests;

// Sugg-4: the schedule behind planned transactions. Pure maths with "today"
// passed in, so the cases that only happen twice a decade (a yearly anchored to
// 29 February, a week that straddles new year) are testable without waiting for
// them or standing up a container.
public class PlannedScheduleTests
{
    private static PlannedTransaction Template(
        Recurrence recurrence, DateOnly startDate, DateOnly? endDate = null) =>
        new()
        {
            UserId = "u",
            Title = "Rent",
            Amount = 800m,
            Kind = PlannedKind.Expense,
            Recurrence = recurrence,
            StartDate = startDate,
            EndDate = endDate
        };

    private static List<DateOnly> DueDates(PlannedTransaction template, DateOnly today) =>
        PlannedSchedule.DueOccurrences(template, today).Select(o => o.DueDate).ToList();

    private static List<string> Keys(PlannedTransaction template, DateOnly today) =>
        PlannedSchedule.DueOccurrences(template, today).Select(o => o.PeriodKey).ToList();

    [Fact]
    public void Monthly_anchored_to_the_31st_returns_to_the_31st_after_a_short_month()
    {
        // The trap: adding a month to the *clamped* February date would pin the
        // schedule to the 28th for the rest of the template's life.
        var template = Template(Recurrence.Monthly, new DateOnly(2026, 1, 31));

        var dates = DueDates(template, new DateOnly(2026, 4, 5));

        Assert.Equal(
            [new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31)],
            dates);
    }

    [Fact]
    public void Monthly_clamps_to_the_30th_in_a_thirty_day_month()
    {
        var template = Template(Recurrence.Monthly, new DateOnly(2026, 3, 31));

        var dates = DueDates(template, new DateOnly(2026, 5, 1));

        Assert.Equal([new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30)], dates);
    }

    [Fact]
    public void Monthly_in_a_leap_February_uses_the_29th()
    {
        var template = Template(Recurrence.Monthly, new DateOnly(2028, 1, 31));

        var dates = DueDates(template, new DateOnly(2028, 3, 1));

        Assert.Equal([new DateOnly(2028, 1, 31), new DateOnly(2028, 2, 29)], dates);
    }

    [Fact]
    public void Yearly_on_29_February_falls_back_to_the_28th_in_a_common_year()
    {
        var template = Template(Recurrence.Yearly, new DateOnly(2024, 2, 29));

        Assert.Equal(
            new DateOnly(2025, 2, 28),
            PlannedSchedule.NextDueAfter(template, new DateOnly(2024, 3, 1)));

        // ...and comes back to the 29th when one is available again.
        Assert.Equal(
            new DateOnly(2028, 2, 29),
            PlannedSchedule.NextDueAfter(template, new DateOnly(2027, 3, 1)));
    }

    [Fact]
    public void A_week_straddling_new_year_is_keyed_by_its_iso_week()
    {
        // Monday 29 December 2025 belongs to ISO week 1 of 2026. Keying it by
        // calendar year would collide with the last week of December 2025.
        Assert.Equal("2026-W01", PlannedSchedule.PeriodKeyFor(Recurrence.Weekly, new DateOnly(2025, 12, 29)));
        Assert.Equal("2025-W52", PlannedSchedule.PeriodKeyFor(Recurrence.Weekly, new DateOnly(2025, 12, 22)));
    }

    [Fact]
    public void Weekly_period_keys_are_distinct_for_every_occurrence()
    {
        var template = Template(Recurrence.Weekly, new DateOnly(2025, 12, 8));

        var keys = Keys(template, new DateOnly(2026, 1, 20));

        // Distinctness is the whole contract: the unique index is what stops a
        // period being queued twice, and it can only work if the key really is
        // one per occurrence.
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Contains("2026-W01", keys);
    }

    [Fact]
    public void Monthly_and_yearly_keys_name_the_period_not_the_date()
    {
        Assert.Equal("2026-02", PlannedSchedule.PeriodKeyFor(Recurrence.Monthly, new DateOnly(2026, 2, 28)));
        Assert.Equal("2026", PlannedSchedule.PeriodKeyFor(Recurrence.Yearly, new DateOnly(2026, 7, 4)));
    }

    [Fact]
    public void A_one_off_has_a_single_occurrence_whose_key_does_not_move_with_its_date()
    {
        var template = Template(Recurrence.Once, new DateOnly(2026, 3, 3));

        var occurrences = PlannedSchedule.DueOccurrences(template, new DateOnly(2026, 6, 1)).ToList();

        Assert.Single(occurrences);
        // Keyed by "there is only one", not by the date: editing StartDate must
        // not let a second one through the unique index.
        Assert.Equal("once", occurrences[0].PeriodKey);
    }

    [Fact]
    public void Nothing_is_due_before_the_start_date()
    {
        var template = Template(Recurrence.Monthly, new DateOnly(2026, 9, 1));

        Assert.Empty(DueDates(template, new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void Nothing_is_due_after_the_end_date()
    {
        var template = Template(
            Recurrence.Monthly, new DateOnly(2026, 1, 15), endDate: new DateOnly(2026, 3, 1));

        var dates = DueDates(template, new DateOnly(2026, 6, 1));

        Assert.Equal([new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15)], dates);
        Assert.Null(PlannedSchedule.NextDueAfter(template, new DateOnly(2026, 2, 15)));
    }

    [Fact]
    public void Future_occurrences_are_never_queued_ahead_of_time()
    {
        var template = Template(Recurrence.Monthly, new DateOnly(2026, 1, 10));

        var dates = DueDates(template, new DateOnly(2026, 3, 10));

        // The 10th of March is due; April is not, however close it looks. The
        // dashboard's look-ahead is computed from NextDueAfter instead.
        Assert.Equal(new DateOnly(2026, 3, 10), dates[^1]);
        Assert.Equal(new DateOnly(2026, 4, 10), PlannedSchedule.NextDueAfter(template, new DateOnly(2026, 3, 10)));
    }

    [Fact]
    public void A_long_backdated_template_does_not_queue_years_of_reminders()
    {
        // "This rent has been running since 2020" means the schedule is
        // historical, not that the user wants seventy things to confirm.
        var template = Template(Recurrence.Monthly, new DateOnly(2020, 1, 1));
        var today = new DateOnly(2026, 7, 28);

        var dates = DueDates(template, today);

        Assert.All(dates, date => Assert.True(date >= today.AddDays(-PlannedSchedule.MaxLookbackDays)));
        Assert.Equal(12, dates.Count);
        Assert.Equal(new DateOnly(2026, 7, 1), dates[^1]);
    }

    [Fact]
    public void A_paused_templates_schedule_is_unchanged_by_pausing()
    {
        // IsActive is enforced by the generator, not the maths — this pins that
        // the schedule itself stays pure so un-pausing resumes where it was.
        var template = Template(Recurrence.Monthly, new DateOnly(2026, 1, 10));
        template.IsActive = false;

        Assert.NotEmpty(DueDates(template, new DateOnly(2026, 3, 10)));
    }
}
