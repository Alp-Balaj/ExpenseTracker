using System.Globalization;
using ExpenseTracker.Application.Services;
using Xunit;

namespace ExpenseTracker.Tests.UnitTests;

/// <summary>
/// Suggestion-6's parser, where all the ways a bank export can lie live.
/// <para>
/// The note's own framing is that a silent misimport destroys trust in the
/// numbers more thoroughly than no import at all — so almost every case here is
/// about a value that <em>would</em> parse into something plausible and wrong:
/// a German thousands separator read as a decimal point, an accounting negative
/// read as positive, a quoted memo containing a comma shifting every later
/// column by one.
/// </para>
/// </summary>
public sealed class CsvStatementParserTests
{
    private static readonly CultureInfo German = new("de-DE");
    private static readonly CultureInfo American = new("en-US");

    // ── Delimiters ─────────────────────────────────────────────────────────

    [Fact]
    public void A_semicolon_file_is_not_read_as_a_comma_file()
    {
        // The trap: the description column is full of commas, so counting raw
        // frequency picks the wrong one and every row collapses.
        const string content =
            "Datum;Betrag;Verwendungszweck\n" +
            "01.08.2026;-12,50;REWE MARKT, BERLIN\n" +
            "02.08.2026;-3,20;BAECKEREI, MITTE\n";

        Assert.Equal(';', CsvStatementParser.DetectDelimiter(content));
    }

    [Theory]
    [InlineData("a,b,c\n1,2,3\n", ',')]
    [InlineData("a\tb\tc\n1\t2\t3\n", '\t')]
    [InlineData("a|b|c\n1|2|3\n", '|')]
    public void Common_delimiters_are_detected(string content, char expected)
    {
        Assert.Equal(expected, CsvStatementParser.DetectDelimiter(content));
    }

    [Fact]
    public void A_single_column_file_falls_back_to_comma_rather_than_throwing()
    {
        Assert.Equal(',', CsvStatementParser.DetectDelimiter("amount\n12.50\n"));
    }

    // ── Quoting ────────────────────────────────────────────────────────────

    [Fact]
    public void A_quoted_field_may_contain_the_delimiter()
    {
        var rows = CsvStatementParser.SplitRows("date,amount,memo\n2026-08-01,-12.50,\"SUPERMARKET, BERLIN\"\n", ',');

        Assert.Equal(2, rows.Count);
        // Three fields, not four: without quote handling every column after the
        // memo shifts by one, silently, for that row only.
        Assert.Equal(3, rows[1].Length);
        Assert.Equal("SUPERMARKET, BERLIN", rows[1][2]);
    }

    [Fact]
    public void A_quoted_field_may_contain_a_doubled_quote_and_a_newline()
    {
        var rows = CsvStatementParser.SplitRows("a,b\n1,\"say \"\"hi\"\"\nsecond line\"\n", ',');

        Assert.Equal(2, rows.Count);
        Assert.Equal("say \"hi\"\nsecond line", rows[1][1]);
    }

    [Fact]
    public void Blank_padding_rows_are_dropped()
    {
        var rows = CsvStatementParser.SplitRows("a,b\n1,2\n,\n\n", ',');

        // Banks pad exports with empty lines; a row of empty strings is not a
        // transaction and must not become a problem row the user has to dismiss.
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void A_byte_order_mark_does_not_become_part_of_the_first_header()
    {
        var rows = CsvStatementParser.SplitRows("﻿Date,Amount\n2026-08-01,1\n", ',');

        Assert.Equal("Date", rows[0][0]);
    }

    // ── Amounts ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("-1.234,56", -1234.56)]
    [InlineData("12,50", 12.50)]
    [InlineData("1.234.567,89", 1234567.89)]
    public void German_amounts_parse_as_German(string raw, double expected)
    {
        Assert.True(CsvStatementParser.TryParseAmount(raw, German, out var amount));
        // The failure this prevents: 1.234,56 read as 1.23 — a hundredth of the
        // real figure, and entirely plausible-looking in a list.
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("-1,234.56", -1234.56)]
    [InlineData("12.50", 12.50)]
    public void American_amounts_parse_as_American(string raw, double expected)
    {
        Assert.True(CsvStatementParser.TryParseAmount(raw, American, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("(123.45)", -123.45)]   // accounting style
    [InlineData("123.45-", -123.45)]    // trailing minus, common in older exports
    [InlineData("$-42.00", -42.00)]
    [InlineData("EUR 1,234.56", 1234.56)]
    [InlineData("1 234.56", 1234.56)]   // non-breaking space as a thousands separator
    public void The_ways_banks_write_a_negative_or_decorate_a_figure_all_parse(string raw, double expected)
    {
        Assert.True(CsvStatementParser.TryParseAmount(raw, American, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("n/a")]
    [InlineData("pending")]
    public void An_unreadable_amount_fails_rather_than_becoming_zero(string raw)
    {
        // A zero would be added to the account silently and balance to nothing.
        Assert.False(CsvStatementParser.TryParseAmount(raw, American, out _));
    }

    [Fact]
    public void A_culture_that_does_not_match_the_file_still_reads_an_unambiguous_figure()
    {
        // The profile says American, the file is German. Rejecting the row would
        // be pedantic when the value can only mean one thing.
        Assert.True(CsvStatementParser.TryParseAmount("1.234,56", American, out var amount));
        Assert.Equal(1234.56m, amount);
    }

    // ── Dates ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-08-01")]
    [InlineData("2026/08/01")]
    [InlineData("20260801")]
    public void Iso_dates_parse_regardless_of_culture(string raw)
    {
        Assert.True(CsvStatementParser.TryParseDate(raw, American, null, out var date));
        Assert.Equal(new DateOnly(2026, 8, 1), date);
    }

    [Fact]
    public void A_dotted_german_date_parses_even_under_an_american_profile()
    {
        Assert.True(CsvStatementParser.TryParseDate("01.08.2026", American, null, out var date));
        Assert.Equal(new DateOnly(2026, 8, 1), date);
    }

    [Fact]
    public void The_ambiguous_slash_date_is_resolved_by_the_profiles_culture_and_never_guessed()
    {
        // The one ambiguity that silently corrupts a whole month of rows, so it is
        // pinned in both directions.
        Assert.True(CsvStatementParser.TryParseDate("03/04/2026", American, null, out var us));
        Assert.Equal(new DateOnly(2026, 3, 4), us);

        Assert.True(CsvStatementParser.TryParseDate("03/04/2026", German, null, out var de));
        Assert.Equal(new DateOnly(2026, 4, 3), de);
    }

    [Fact]
    public void An_explicit_format_wins_over_the_culture()
    {
        Assert.True(CsvStatementParser.TryParseDate("04/03/2026", American, "dd/MM/yyyy", out var date));
        Assert.Equal(new DateOnly(2026, 3, 4), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a date")]
    [InlineData("31.02.2026")]
    public void An_unreadable_date_fails_rather_than_defaulting_to_today(string raw)
    {
        Assert.False(CsvStatementParser.TryParseDate(raw, German, null, out _));
    }

    // ── Descriptions and duplicates ────────────────────────────────────────

    [Fact]
    public void Cosmetically_different_memos_normalize_to_the_same_thing()
    {
        // A re-issued statement changes punctuation and spacing; comparing raw
        // strings would let a re-import through as a page of new rows.
        var a = CsvStatementParser.NormalizeDescription("REWE  MARKT / BERLIN-MITTE");
        var b = CsvStatementParser.NormalizeDescription("rewe markt, berlin mitte");

        Assert.Equal(a, b);
        Assert.Equal("rewe markt berlin mitte", a);
    }

    [Fact]
    public void Genuinely_different_memos_stay_different()
    {
        Assert.NotEqual(
            CsvStatementParser.NormalizeDescription("REWE MARKT"),
            CsvStatementParser.NormalizeDescription("EDEKA MARKT"));
    }

    [Fact]
    public void An_empty_memo_normalizes_to_empty_rather_than_throwing()
    {
        Assert.Equal(string.Empty, CsvStatementParser.NormalizeDescription(null));
        Assert.Equal(string.Empty, CsvStatementParser.NormalizeDescription("  ---  "));
    }

    // ── Column suggestion ──────────────────────────────────────────────────

    [Fact]
    public void English_headers_are_recognised()
    {
        var (date, amount, description, counterparty) =
            CsvStatementParser.SuggestColumns(["Transaction Date", "Description", "Amount", "Payee"]);

        Assert.Equal(0, date);
        Assert.Equal(2, amount);
        Assert.Equal(1, description);
        Assert.Equal(3, counterparty);
    }

    [Fact]
    public void German_headers_are_recognised()
    {
        var (date, amount, description, _) =
            CsvStatementParser.SuggestColumns(["Buchungstag", "Verwendungszweck", "Betrag"]);

        Assert.Equal(0, date);
        Assert.Equal(1, description);
        Assert.Equal(2, amount);
    }

    [Fact]
    public void An_unrecognisable_header_suggests_nothing_rather_than_something_wrong()
    {
        var (date, amount, description, counterparty) =
            CsvStatementParser.SuggestColumns(["col1", "col2", "col3"]);

        // A wrong suggestion the user accepts without reading is worse than none.
        Assert.Null(date);
        Assert.Null(amount);
        Assert.Null(description);
        Assert.Null(counterparty);
    }
}
