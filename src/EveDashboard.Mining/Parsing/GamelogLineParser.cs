namespace EveDashboard.Mining.Parsing;

using System.Globalization;
using System.Text.RegularExpressions;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IGamelogLineParser"/> implementation. Recognizes the
/// three v1 mining-line shapes via pre-compiled regexes; resolves the captured
/// product name through <see cref="ISdeLookup"/>; emits typed
/// <see cref="MiningEvent"/> records.
/// </summary>
/// <remarks>
/// Regex shapes derive from real EVE gamelogs (see fixtures under
/// <c>tests/EveDashboard.Mining.Tests/testdata/Gamelogs/</c>) and are inspired by
/// the reference project at <c>../Mining-Dashboard/mining_dashboard.py:683-689</c>;
/// every pattern was verified against the actual logs at plan-10 drafting time.
/// </remarks>
public sealed class GamelogLineParser : IGamelogLineParser
{
    /// <summary>
    /// Channel marker present on every mining log line. Used as an
    /// <see cref="string.IndexOf(string, StringComparison)"/> pre-filter so
    /// the 99%+ majority of non-mining log lines short-circuit before any
    /// regex evaluation.
    /// </summary>
    private const string MiningChannelMarker = "(mining)";

    /// <summary>EVE log timestamp format used by <see cref="DateTimeOffset.ParseExact(string, string, IFormatProvider, DateTimeStyles)"/>.</summary>
    private const string TimestampFormat = "yyyy.MM.dd HH:mm:ss";

    /// <summary>
    /// Captures the leading <c>[ YYYY.MM.DD HH:MM:SS ]</c> timestamp on every
    /// EVE log line. Group <c>ts</c> contains the date+time, ready for
    /// <see cref="DateTimeOffset.ParseExact(string, string, IFormatProvider, DateTimeStyles)"/>.
    /// </summary>
    private static readonly Regex TimestampPattern = new(
        @"^\[\s*(?<ts>\d{4}\.\d{2}\.\d{2}\s+\d{2}:\d{2}:\d{2})\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Mining-tick pattern: <c>You mined &lt;font size=12&gt;&lt;color=...&gt;N&lt;color=...&gt;&lt;font size=10&gt; units of &lt;color=...&gt;&lt;font size=12&gt;&lt;product&gt;</c>.
    /// Captures <c>amt</c> (units mined) and <c>name</c> (product name through end-of-line / next tag).
    /// </summary>
    private static readonly Regex TickPattern = new(
        @"You mined <font size=12><color=[^>]+>(?<amt>\d+)<color=[^>]+><font size=10> units of <color=[^>]+><font size=12>(?<name>[^\r\n<]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Critical-hit pattern: <c>You mined an additional &lt;color=...&gt;&lt;font size=12&gt;N&lt;color=...&gt;&lt;font size=10&gt; units of ...</c>.
    /// Note the color/font ordering differs from the tick pattern (color-then-font here, font-then-color above).
    /// </summary>
    private static readonly Regex CritPattern = new(
        @"You mined an additional <color=[^>]+><font size=12>(?<amt>\d+)<color=[^>]+><font size=10> units of <color=[^>]+><font size=12>(?<name>[^\r\n<]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Residue pattern: <c>Additional &lt;font size=12&gt;&lt;color=...&gt;N&lt;color=...&gt;&lt;font size=10&gt; units depleted from asteroid as residue</c>.
    /// Captures <c>amt</c> only — the EVE residue line carries no product name.
    /// </summary>
    private static readonly Regex ResiduePattern = new(
        @"Additional <font size=12><color=[^>]+>(?<amt>\d+)<color=[^>]+><font size=10> units depleted from asteroid as residue",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ISdeLookup _sde;
    private readonly ILogger<GamelogLineParser> _log;

    /// <summary>
    /// Set of unresolved product names already warned about (case-insensitive).
    /// Bounds the log volume when a session mines hundreds of ticks of an
    /// unknown ore: one Warning per distinct name, not one per tick.
    /// </summary>
    private readonly HashSet<string> _unresolvedWarned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Construct a parser.</summary>
    /// <param name="sde">SDE lookup used to resolve product names to type ids.</param>
    /// <param name="log">Logger for malformed-line and unresolved-name diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown if any argument is null.</exception>
    public GamelogLineParser(ISdeLookup sde, ILogger<GamelogLineParser> log)
    {
        ArgumentNullException.ThrowIfNull(sde);
        ArgumentNullException.ThrowIfNull(log);
        _sde = sde;
        _log = log;
    }

    /// <inheritdoc />
    public MiningEvent? Parse(string line, long characterId)
    {
        ArgumentNullException.ThrowIfNull(line);

        // Pre-filter: 99%+ of EVE log lines are non-mining (chat, jumps, hints, notify).
        // Cheap IndexOf beats four regex evaluations on the rejection path.
        if (line.IndexOf(MiningChannelMarker, StringComparison.Ordinal) < 0)
        {
            return null;
        }

        var ts = TryParseTimestamp(line);
        if (ts is null)
        {
            return null;
        }

        // Order matters: a Crit line contains the literal "You mined" too, but
        // the Tick pattern's anchor "You mined <font size=12>" does NOT match a
        // Crit line ("You mined an additional <color=..."). Evaluating Crit
        // first is belt-and-braces against any future regex tweak.
        return TryParseEvent(CritPattern, line, MiningEventKind.Crit, ts.Value, characterId)
            ?? TryParseEvent(TickPattern, line, MiningEventKind.Tick, ts.Value, characterId)
            ?? TryParseResidue(line, ts.Value, characterId);
    }

    /// <summary>
    /// Try to match a tick or crit pattern against <paramref name="line"/> and,
    /// on success, build the corresponding <see cref="MiningEvent"/>.
    /// </summary>
    /// <param name="pattern">Either <see cref="TickPattern"/> or <see cref="CritPattern"/>.</param>
    /// <param name="line">Source log line.</param>
    /// <param name="kind">Either <see cref="MiningEventKind.Tick"/> or <see cref="MiningEventKind.Crit"/>.</param>
    /// <param name="timestamp">Already-parsed event timestamp.</param>
    /// <param name="characterId">EVE character id supplied by the caller.</param>
    /// <returns>The parsed event, or null if the pattern did not match.</returns>
    private MiningEvent? TryParseEvent(
        Regex pattern,
        string line,
        MiningEventKind kind,
        DateTimeOffset timestamp,
        long characterId)
    {
        var m = pattern.Match(line);
        if (!m.Success)
        {
            return null;
        }

        if (!long.TryParse(m.Groups["amt"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            _log.LogWarning(
                "Gamelog line matched {Kind} shape but the amount capture '{Amount}' did not parse as long: {Line}",
                kind, m.Groups["amt"].Value, line);
            return null;
        }

        var name = m.Groups["name"].Value.Trim();
        var typeId = _sde.TryGetTypeId(name);
        if (typeId is null && _unresolvedWarned.Add(name))
        {
            _log.LogWarning(
                "Gamelog product name '{Name}' not found in the SDE lookup (further occurrences in this session will be silent).",
                name);
        }

        return new MiningEvent(
            Kind: kind,
            EveTimestamp: timestamp,
            CharacterId: characterId,
            ProductName: name,
            TypeId: typeId,
            Amount: amount);
    }

    /// <summary>
    /// Try to match the residue pattern against <paramref name="line"/> and,
    /// on success, build a <see cref="MiningEventKind.Residue"/> event.
    /// Residue events always carry <c>ProductName = ""</c> and <c>TypeId = null</c>
    /// because the EVE residue line carries no ore name.
    /// </summary>
    /// <param name="line">Source log line.</param>
    /// <param name="timestamp">Already-parsed event timestamp.</param>
    /// <param name="characterId">EVE character id supplied by the caller.</param>
    /// <returns>The parsed residue event, or null if the pattern did not match.</returns>
    private MiningEvent? TryParseResidue(string line, DateTimeOffset timestamp, long characterId)
    {
        var m = ResiduePattern.Match(line);
        if (!m.Success)
        {
            return null;
        }

        if (!long.TryParse(m.Groups["amt"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            _log.LogWarning(
                "Gamelog line matched Residue shape but the amount capture '{Amount}' did not parse as long: {Line}",
                m.Groups["amt"].Value, line);
            return null;
        }

        return new MiningEvent(
            Kind: MiningEventKind.Residue,
            EveTimestamp: timestamp,
            CharacterId: characterId,
            ProductName: string.Empty,
            TypeId: null,
            Amount: amount);
    }

    /// <summary>
    /// Extract the leading <c>[ YYYY.MM.DD HH:MM:SS ]</c> timestamp and parse it
    /// as UTC. EVE log time is UTC; the resulting <see cref="DateTimeOffset"/>
    /// has <see cref="TimeSpan.Zero"/> offset.
    /// </summary>
    /// <param name="line">Source log line.</param>
    /// <returns>The parsed timestamp, or null if the line does not start with a recognizable timestamp.</returns>
    private static DateTimeOffset? TryParseTimestamp(string line)
    {
        var m = TimestampPattern.Match(line);
        if (!m.Success)
        {
            return null;
        }

        // The captured group has one literal space between date and time (real
        // EVE always emits exactly one). ParseExact's format string requires
        // exactly one space, so any "double space" log would fail to parse here
        // and the line would be dropped silently — acceptable loud-fail behavior.
        if (DateTimeOffset.TryParseExact(
                m.Groups["ts"].Value,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
