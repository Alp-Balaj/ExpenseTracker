using System.Globalization;
using System.Text;

namespace ExpenseTracker.Application.Services;

/// <summary>
/// Reads a bank's CSV export into rows of strings, and turns those strings into
/// dates and amounts.
///
/// <para>
/// Pure: no database, no clock, no configuration of its own. Everything that
/// varies between banks — the delimiter, the decimal separator, how a negative
/// is written, whether the file starts with three lines of preamble — is either
/// passed in or sniffed from the text, which is what makes the awkward cases
/// (a German statement with <c>1.234,56</c>, an American one with
/// <c>(1,234.56)</c> for a debit, a trailing-minus export) unit-testable without
/// a container.
/// </para>
///
/// <para>
/// Sugg-6's premise is that a silent misimport destroys trust in the numbers
/// more thoroughly than no import at all. So every conversion here fails loudly
/// and returns false rather than guessing: an unparseable row becomes a problem
/// the user sees in the preview, never a zero quietly added to a balance.
/// </para>
/// </summary>
public static class CsvStatementParser
{
    /// <summary>Delimiters worth sniffing, in the order they are preferred on a tie.</summary>
    private static readonly char[] Candidates = [';', ',', '\t', '|'];

    /// <summary>
    /// Date formats tried before falling back to the culture's own patterns.
    /// Ordered least-ambiguous first: an ISO date can only mean one thing, and
    /// <c>03/04/2026</c> is deliberately left to the culture rather than guessed.
    /// </summary>
    private static readonly string[] UnambiguousDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyyMMdd",
        "dd.MM.yyyy",
        "dd.MM.yy",
        "d.M.yyyy"
    ];

    /// <summary>
    /// Guesses the delimiter from the first few lines: the candidate that appears
    /// the same non-zero number of times on every line wins.
    /// <para>
    /// Consistency rather than raw frequency, because a description field full of
    /// commas will out-count the semicolons that actually separate the columns.
    /// </para>
    /// </summary>
    public static char DetectDelimiter(string content)
    {
        var lines = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Take(10)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count == 0) return ',';

        var best = ',';
        var bestScore = -1;

        foreach (var candidate in Candidates)
        {
            var counts = lines.Select(line => CountOutsideQuotes(line, candidate)).ToList();
            if (counts[0] == 0) continue;

            var consistent = counts.All(c => c == counts[0]);
            // A consistent delimiter beats an inconsistent one at any frequency;
            // among consistent ones, more columns wins.
            var score = (consistent ? 1000 : 0) + counts[0];

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (line[i] == delimiter && !inQuotes) count++;
        }

        return count;
    }

    /// <summary>
    /// Splits the whole document into rows of fields, honouring RFC 4180 quoting:
    /// a quoted field may contain the delimiter, a newline, and doubled quotes.
    /// <para>
    /// Hand-written rather than <c>string.Split</c> because a memo line like
    /// <c>"SUPERMARKET, BERLIN"</c> is extremely normal in a statement and
    /// splitting naively shifts every column after it by one — silently, for that
    /// row only.
    /// </para>
    /// </summary>
    public static List<string[]> SplitRows(string content, char delimiter)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        // A BOM survives File.ReadAllText in some encodings and would otherwise
        // become part of the first header cell, which then matches nothing.
        var text = content.TrimStart('﻿');

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            if (c == '"') inQuotes = true;
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                rows.Add([.. fields]);
                fields.Clear();
            }
            else if (c != '\r') field.Append(c);
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add([.. fields]);
        }

        // Banks pad their exports with trailing blank lines; a row of empty
        // strings is not a transaction.
        return rows
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToList();
    }

    /// <summary>
    /// Parses a money column. Returns false rather than a guess whenever the text
    /// is not unambiguously a number.
    /// </summary>
    /// <remarks>
    /// Handles, because real exports contain all of them: thousands separators in
    /// either convention, a currency symbol or code glued to the figure,
    /// non-breaking spaces, an accounting-style <c>(123.45)</c> negative, and a
    /// trailing minus.
    /// </remarks>
    public static bool TryParseAmount(string? raw, CultureInfo culture, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var text = raw.Trim().Replace(' ', ' ').Replace(' ', ' ');

        var negative = false;

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            negative = true;
            text = text[1..^1].Trim();
        }

        if (text.EndsWith('-'))
        {
            negative = true;
            text = text[..^1].Trim();
        }

        // Strip anything that is not a digit, separator or sign — "EUR 1.234,56",
        // "1 234,56 €" and "$-42.00" all reduce to something parseable.
        var cleaned = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsDigit(c) || c is '.' or ',' or '-' or '+' or ' ')
                cleaned.Append(c);
        }

        text = cleaned.ToString().Replace(" ", string.Empty).Trim();
        if (text.Length == 0) return false;

        if (text.StartsWith('-'))
        {
            negative = !negative;
            text = text[1..];
        }
        else if (text.StartsWith('+')) text = text[1..];

        if (text.Length == 0) return false;

        var style = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands;

        if (!decimal.TryParse(text, style, culture, out amount))
        {
            // The culture is the profile's claim about the file, not a guarantee.
            // Fall back to the other convention rather than rejecting a row the
            // user can plainly read.
            // GetCultureInfo is override-free: this fallback exists to represent
            // the comma-decimal convention, so it must not pick up whatever
            // separators the host user configured for their own desktop.
            var fallback = culture.NumberFormat.NumberDecimalSeparator == ","
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo("de-DE");

            if (!decimal.TryParse(text, style, fallback, out amount))
                return false;
        }

        if (negative) amount = -amount;
        return true;
    }

    /// <summary>
    /// Parses a date column, preferring an explicit format, then the unambiguous
    /// patterns, then the culture's own.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="DateOnly"/> and never a <see cref="DateTime"/>: Fix-14's
    /// whole point was that a statement date has no time and no zone, and the
    /// moment one acquires either it starts moving by a day.
    /// </remarks>
    public static bool TryParseDate(string? raw, CultureInfo culture, string? explicitFormat, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var text = raw.Trim();

        if (!string.IsNullOrWhiteSpace(explicitFormat)
            && DateTime.TryParseExact(text, explicitFormat, culture, DateTimeStyles.None, out var exact))
        {
            date = DateOnly.FromDateTime(exact);
            return true;
        }

        if (DateTime.TryParseExact(text, UnambiguousDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var known))
        {
            date = DateOnly.FromDateTime(known);
            return true;
        }

        // 03/04/2026 is 3 April in de-DE/en-GB and 4 March in en-US. Deliberately
        // resolved by the profile's culture and never by a guess — this is the one
        // ambiguity that silently corrupts a whole month of rows.
        if (DateTime.TryParse(text, culture, DateTimeStyles.None, out var cultural))
        {
            date = DateOnly.FromDateTime(cultural);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A description reduced to what two rows would have to share to be the same
    /// transaction: lowercase, alphanumerics only, single-spaced.
    /// <para>
    /// Banks re-issue the same statement with cosmetically different memo text
    /// (extra spaces, a changed reference suffix's punctuation), so comparing raw
    /// strings would let a re-import through as new rows.
    /// </para>
    /// </summary>
    public static string NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Guesses which column is which from the header text, in several languages,
    /// so the mapping screen opens with something sensible already selected.
    /// A guess only — every value is editable before anything is imported.
    /// </summary>
    public static (int? Date, int? Amount, int? Description, int? Counterparty) SuggestColumns(string[] header)
    {
        int? date = null, amount = null, description = null, counterparty = null;

        for (var i = 0; i < header.Length; i++)
        {
            var name = NormalizeDescription(header[i]);
            if (name.Length == 0) continue;

            if (date is null && Matches(name, "date", "datum", "buchungstag", "valuta", "fecha", "transaction date"))
                date = i;
            else if (amount is null && Matches(name, "amount", "betrag", "value", "importe", "umsatz", "sum"))
                amount = i;
            else if (description is null && Matches(name, "description", "verwendungszweck", "reference", "memo", "details", "text", "narrative"))
                description = i;
            else if (counterparty is null && Matches(name, "payee", "counterparty", "beguenstigter", "empfaenger", "auftraggeber", "merchant", "name"))
                counterparty = i;
        }

        return (date, amount, description, counterparty);
    }

    private static bool Matches(string normalizedHeader, params string[] needles) =>
        needles.Any(needle => normalizedHeader.Contains(needle, StringComparison.Ordinal));
}
