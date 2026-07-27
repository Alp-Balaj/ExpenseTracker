using ExpenseTracker.Application.Services;
using Xunit;

namespace ExpenseTracker.Tests.UnitTests;

// The status endpoint's month is a yyyy-MM wire value parsed with
// InvariantCulture; a culture-sensitive parse would read "2026-07" differently
// on a server whose default culture uses another calendar or separator.
public sealed class BudgetMonthParsingTests
{
    [Theory]
    [InlineData("2026-07", 2026, 7)]
    [InlineData("2026-01", 2026, 1)]
    [InlineData("1999-12", 1999, 12)]
    public void Parses_valid_year_month(string value, int expectedYear, int expectedMonth)
    {
        Assert.True(BudgetService.TryParseMonth(value, out var year, out var month));
        Assert.Equal(expectedYear, year);
        Assert.Equal(expectedMonth, month);
    }

    [Theory]
    [InlineData("2026-07-15")]  // full date, not a month
    [InlineData("2026-13")]     // month out of range
    [InlineData("07-2026")]     // reversed
    [InlineData("2026/07")]
    [InlineData("")]
    [InlineData("not-a-month")]
    public void Rejects_anything_that_is_not_year_month(string value)
    {
        Assert.False(BudgetService.TryParseMonth(value, out _, out _));
    }
}
