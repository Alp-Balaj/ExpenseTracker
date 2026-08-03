using System.Globalization;
using System.Text;

namespace ExpenseTracker.Application.Services;

/// <summary>
/// Writing CSV that a spreadsheet will open without either mangling it or
/// executing it.
/// <para>
/// Extracted from <c>ReportsController</c> so the report export and the
/// full-account export (C·4) cannot drift apart on the one rule that is a
/// security control rather than a formatting choice — see <see cref="Escape"/>.
/// </para>
/// </summary>
public static class CsvWriter
{
    /// <summary>
    /// Quotes a field, and neutralises spreadsheet formula injection.
    /// <para>
    /// A cell beginning <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is executed as a
    /// formula by Excel, Sheets and LibreOffice alike. Since an export contains
    /// text the user never wrote — a bank's memo line, straight out of a CSV
    /// import — that is a live path from a statement into a formula on someone's
    /// desktop. A leading apostrophe stops it and is invisible in the cell.
    /// </para>
    /// </summary>
    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
            text = "'" + text;

        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
            return $"\"{text.Replace("\"", "\"\"")}\"";

        return text;
    }

    /// <summary>
    /// Joins one row. Strings are escaped; numbers are written raw, in invariant
    /// format.
    /// <para>
    /// The distinction is not cosmetic. A negative amount starts with <c>-</c>,
    /// which <see cref="Escape"/> treats as a formula and prefixes with an
    /// apostrophe — turning every expense in the sheet into <em>text</em>, so the
    /// column will not sum. The guard exists for values a person or a bank wrote;
    /// a decimal this code formatted itself is not one of them.
    /// </para>
    /// </summary>
    public static void AppendRow(StringBuilder builder, params object?[] values)
    {
        builder.AppendLine(string.Join(',', values.Select(Cell)));
    }

    private static string Cell(object? value) => value switch
    {
        null => string.Empty,
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        double dbl => dbl.ToString(CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string s => Escape(s),
        _ => Escape(value.ToString())
    };
}
