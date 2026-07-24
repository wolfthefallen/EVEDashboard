namespace EveDashboard.App.Wpf.Reporting.HaulValue;

using System.Collections.Generic;
using System.Globalization;

/// <summary>One parsed paste line: an item display name + a unit quantity.</summary>
/// <param name="Name">Item display name exactly as pasted (trimmed).</param>
/// <param name="Quantity">Unit count (defaults to 1 when the line carries no number).</param>
public sealed record PasteLine(string Name, long Quantity);

/// <summary>
/// Parses text pasted from EVE into (name, quantity) lines for the Haul Value
/// window. Handles the two common shapes the client produces / players use:
/// <list type="bullet">
///   <item>TAB-separated (inventory copy): <c>Name\tQuantity\tGroup\t...</c> — the
///     name is the first field and the quantity is the first later field that reads
///     as an integer.</item>
///   <item>Space-separated (hand-typed / multibuy): <c>Name 1000</c>, <c>Name x1000</c>,
///     or just <c>Name</c> (quantity 1).</item>
/// </list>
/// Quantities may carry thousands separators (<c>1,000</c> / <c>1.000</c> / <c>1 000</c>)
/// — all non-digit characters in the quantity token are stripped before parsing,
/// which is safe because EVE unit counts are whole numbers. Name resolution to a
/// type id is a separate step (<see cref="EveDashboard.Core.Lookup.ISdeTypeNameResolver"/>).
/// </summary>
public static class EveInventoryPasteParser
{
    /// <summary>
    /// Parse <paramref name="text"/> into one <see cref="PasteLine"/> per non-blank
    /// input line. Never throws; blank lines and lines with an empty name are skipped.
    /// </summary>
    /// <param name="text">Raw pasted text (may be null/empty).</param>
    /// <returns>The parsed lines, in input order.</returns>
    public static IReadOnlyList<PasteLine> Parse(string? text)
    {
        var result = new List<PasteLine>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            string name;
            long quantity;
            if (line.Contains('\t'))
            {
                var fields = line.Split('\t');
                name = fields[0].Trim();
                quantity = 1;
                for (var i = 1; i < fields.Length; i++)
                {
                    if (TryExtractQuantity(fields[i], out var q)) { quantity = q; break; }
                }
            }
            else
            {
                (name, quantity) = SplitTrailingQuantity(line);
            }

            if (name.Length == 0 || quantity <= 0) continue;
            result.Add(new PasteLine(name, quantity));
        }
        return result;
    }

    /// <summary>
    /// Split a space-separated line into (name, quantity). If the last token reads
    /// as an integer (optionally an <c>x</c>-prefixed count), it is the quantity and
    /// the rest is the name; otherwise the whole line is the name with quantity 1.
    /// </summary>
    private static (string Name, long Quantity) SplitTrailingQuantity(string line)
    {
        var lastSpace = line.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            var tail = line[(lastSpace + 1)..];
            if (TryExtractQuantity(tail, out var q))
            {
                return (line[..lastSpace].Trim(), q);
            }
        }
        return (line, 1);
    }

    /// <summary>
    /// Extract a positive integer quantity from <paramref name="token"/> by stripping
    /// every non-digit character (thousands separators, a leading <c>x</c>, etc.).
    /// Returns false when the token has no digits.
    /// </summary>
    private static bool TryExtractQuantity(string token, out long quantity)
    {
        quantity = 0;
        if (string.IsNullOrEmpty(token)) return false;

        var digits = new char[token.Length];
        var n = 0;
        foreach (var c in token)
        {
            if (c >= '0' && c <= '9') digits[n++] = c;
        }
        if (n == 0) return false;
        return long.TryParse(new string(digits, 0, n), NumberStyles.None, CultureInfo.InvariantCulture, out quantity)
               && quantity > 0;
    }
}
