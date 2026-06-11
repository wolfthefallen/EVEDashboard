using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Mining.Parsing;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Mining.Tests.Parsing;

/// <summary>
/// Unit tests for <see cref="GamelogLineParser"/>. Inputs are real-fixture-derived
/// line constants embedded as strings; the test file is self-contained and does
/// not depend on the SDE or on any file under <c>testdata/</c>. The integration
/// resolution test (in <see cref="RealGamelogResolutionTests"/>) is the place
/// where real fixture files and the shipped fallback meet.
/// </summary>
public class GamelogLineParserTests
{
    /// <summary>Real tick line (fixture <c>20260428_224855_90863271.txt</c>:17, redacted character).</summary>
    private const string TickLine =
        "[ 2026.04.28 23:41:45 ] (mining) <color=0x77ffffff>You mined <font size=12><color=#ff8dc169>32<color=0x77ffffff><font size=10> units of <color=0xffffffff><font size=12>Nocxite III-Grade";

    /// <summary>Real crit line (fixture <c>20260428_224858_2123831393.txt</c>:28, redacted character).</summary>
    private const string CritLine =
        "[ 2026.04.28 23:44:01 ] (mining) <color=#fff0ff45>Critical mining success!<color=0x77ffffff><font size=10> You mined an additional <color=#fff0ff45><font size=12>291<color=0x77ffffff><font size=10> units of <color=0xffffffff><font size=12>Nocxite III-Grade";

    /// <summary>Real residue line (fixture <c>20260428_224855_90863271.txt</c>:20).</summary>
    private const string ResidueLine =
        "[ 2026.04.28 23:41:45 ] (mining) <color=0x77ffffff>Additional <font size=12><color=#ffff454b>32<color=0x77ffffff><font size=10> units depleted from asteroid as residue";

    private const long FakeCharacterId = 90863271L;
    private const long NocxiteIIIGradeTypeId = 82018L;

    /// <summary>Stub <see cref="ISdeLookup"/> for unit tests; records calls and returns canned responses.</summary>
    private sealed class StubSdeLookup : ISdeLookup
    {
        public Dictionary<string, long?> NameToTypeId { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> NameLookups { get; } = new();

        public long? TryGetTypeId(string productName)
        {
            ArgumentNullException.ThrowIfNull(productName);
            NameLookups.Add(productName);
            return NameToTypeId.TryGetValue(productName, out var id) ? id : null;
        }

        public MiningProduct? TryGetProduct(long typeId) => null;
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public string? TryGetRefinedMaterialName(long typeId) => null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (GamelogLineParser parser, StubSdeLookup sde, CapturingLogger<GamelogLineParser> log) BuildParser(
        Dictionary<string, long?>? names = null)
    {
        var sde = new StubSdeLookup();
        if (names is not null)
        {
            foreach (var kv in names) sde.NameToTypeId[kv.Key] = kv.Value;
        }
        else
        {
            sde.NameToTypeId["Nocxite III-Grade"] = NocxiteIIIGradeTypeId;
        }
        var log = new CapturingLogger<GamelogLineParser>();
        return (new GamelogLineParser(sde, log), sde, log);
    }

    [Fact]
    public void Parse_TickLine_returns_Tick_event_with_amount_and_resolved_typeid()
    {
        var (parser, _, _) = BuildParser();

        var ev = parser.Parse(TickLine, FakeCharacterId);

        ev.Should().NotBeNull();
        ev!.Kind.Should().Be(MiningEventKind.Tick);
        ev.Amount.Should().Be(32L);
        ev.ProductName.Should().Be("Nocxite III-Grade");
        ev.TypeId.Should().Be(NocxiteIIIGradeTypeId);
        ev.CharacterId.Should().Be(FakeCharacterId);
        ev.EveTimestamp.Should().Be(new DateTimeOffset(2026, 4, 28, 23, 41, 45, TimeSpan.Zero));
    }

    [Fact]
    public void Parse_CritLine_returns_Crit_event_with_bonus_amount()
    {
        var (parser, _, _) = BuildParser();

        var ev = parser.Parse(CritLine, FakeCharacterId);

        ev.Should().NotBeNull();
        ev!.Kind.Should().Be(MiningEventKind.Crit);
        ev.Amount.Should().Be(291L);
        ev.ProductName.Should().Be("Nocxite III-Grade");
        ev.TypeId.Should().Be(NocxiteIIIGradeTypeId);
    }

    [Fact]
    public void Parse_CritLine_does_not_match_TickPattern()
    {
        // Sanity: even though both lines say "You mined", the crit line has
        // "an additional <color=" between "mined" and the digit, while the tick
        // pattern anchors on "You mined <font size=12>". The crit must be
        // classified as Crit, never as Tick.
        var (parser, _, _) = BuildParser();

        var ev = parser.Parse(CritLine, FakeCharacterId);

        ev!.Kind.Should().Be(MiningEventKind.Crit);
        ev.Kind.Should().NotBe(MiningEventKind.Tick);
    }

    [Fact]
    public void Parse_ResidueLine_returns_Residue_event_with_empty_product_name_and_null_typeid()
    {
        var (parser, sde, _) = BuildParser();

        var ev = parser.Parse(ResidueLine, FakeCharacterId);

        ev.Should().NotBeNull();
        ev!.Kind.Should().Be(MiningEventKind.Residue);
        ev.Amount.Should().Be(32L);
        ev.ProductName.Should().Be(string.Empty);
        ev.TypeId.Should().BeNull();
        sde.NameLookups.Should().BeEmpty("residue lines carry no product name; the parser should not attempt SDE resolution");
    }

    [Theory]
    [InlineData("[ 2026.04.28 21:20:57 ] (None) Jumping from Walvalin to Otsela")]
    [InlineData("[ 2026.04.28 21:17:15 ] (hint) Attempting to join a channel")]
    [InlineData("[ 2026.04.24 21:14:04 ] (notify) Trade with Jenn Fallen completed.")]
    public void Parse_NonMiningLine_returns_null(string line)
    {
        var (parser, sde, _) = BuildParser();

        var ev = parser.Parse(line, FakeCharacterId);

        ev.Should().BeNull();
        sde.NameLookups.Should().BeEmpty();
    }

    [Fact]
    public void Parse_LineWithoutMiningChannel_returns_null_without_sde_lookup()
    {
        // Looks vaguely mining-shaped but does NOT carry the (mining) channel.
        // The pre-filter must short-circuit BEFORE any regex evaluation, which
        // is observable here because the SDE stub is never invoked.
        var (parser, sde, _) = BuildParser();
        var line = "[ 2026.04.28 23:41:45 ] (combat) You mined some attacker for 32 damage";

        var ev = parser.Parse(line, FakeCharacterId);

        ev.Should().BeNull();
        sde.NameLookups.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MalformedTimestamp_returns_null_silently()
    {
        var (parser, _, log) = BuildParser();
        var line = "[xxxxxx] (mining) <color=0x77ffffff>You mined <font size=12><color=#ff8dc169>32<color=0x77ffffff><font size=10> units of <color=0xffffffff><font size=12>Nocxite III-Grade";

        var ev = parser.Parse(line, FakeCharacterId);

        ev.Should().BeNull();
        log.Entries.Should().BeEmpty("malformed timestamp on a mining-shaped line is non-mining noise, not a malformed mining event");
    }

    [Fact]
    public void Parse_TickLine_TrimsTrailingWhitespace_OnProductName()
    {
        var (parser, sde, _) = BuildParser(
            new Dictionary<string, long?> { ["Veldspar"] = 1230L });
        var line =
            "[ 2026.04.28 23:41:45 ] (mining) <color=0x77ffffff>You mined <font size=12><color=#ff8dc169>10<color=0x77ffffff><font size=10> units of <color=0xffffffff><font size=12>Veldspar   ";

        var ev = parser.Parse(line, FakeCharacterId);

        ev.Should().NotBeNull();
        ev!.ProductName.Should().Be("Veldspar");
        sde.NameLookups.Should().ContainSingle().Which.Should().Be("Veldspar");
    }

    [Fact]
    public void Parse_TickLine_UnknownProduct_LogsWarningOnce_DeduplicatedAcrossCalls()
    {
        // SDE knows nothing -> every Tick of this name comes back with TypeId=null,
        // but Warning fires only on the FIRST occurrence.
        var (parser, _, log) = BuildParser(new Dictionary<string, long?>());

        var ev1 = parser.Parse(TickLine, FakeCharacterId);
        var ev2 = parser.Parse(TickLine, FakeCharacterId);
        var ev3 = parser.Parse(TickLine, FakeCharacterId);

        ev1!.TypeId.Should().BeNull();
        ev2!.TypeId.Should().BeNull();
        ev3!.TypeId.Should().BeNull();
        ev1.Amount.Should().Be(32L);
        ev2.Amount.Should().Be(32L);

        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(1, "Warning is deduplicated per unresolved name within the parser instance");
        warnings[0].Message.Should().Contain("Nocxite III-Grade");
    }

    [Fact]
    public void Parse_TickLine_UnknownProduct_DifferentNames_LogsWarningPerName()
    {
        var (parser, _, log) = BuildParser(new Dictionary<string, long?>());
        var anotherTickLine = TickLine.Replace("Nocxite III-Grade", "Mystery Ore");

        parser.Parse(TickLine, FakeCharacterId);
        parser.Parse(anotherTickLine, FakeCharacterId);

        var warnings = log.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(2);
        warnings.Select(w => w.Message).Should().ContainMatch("*Nocxite III-Grade*");
        warnings.Select(w => w.Message).Should().ContainMatch("*Mystery Ore*");
    }

    [Fact]
    public void Parse_NullLine_throws_ArgumentNullException()
    {
        var (parser, _, _) = BuildParser();

        Action act = () => parser.Parse(null!, FakeCharacterId);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_EmptyLine_returns_null()
    {
        var (parser, _, _) = BuildParser();

        parser.Parse(string.Empty, FakeCharacterId).Should().BeNull();
    }

    [Fact]
    public void Parse_TickLine_PropagatesCharacterId()
    {
        var (parser, _, _) = BuildParser();
        const long expected = 1234567890L;

        var ev = parser.Parse(TickLine, expected);

        ev!.CharacterId.Should().Be(expected);
    }

    [Fact]
    public void Parse_TickLine_TimestampHasUtcOffset()
    {
        var (parser, _, _) = BuildParser();

        var ev = parser.Parse(TickLine, FakeCharacterId);

        ev!.EveTimestamp.Offset.Should().Be(TimeSpan.Zero, "EVE log time is UTC");
    }

    [Fact]
    public void Constructor_throws_on_null_sde()
    {
        Action act = () => _ = new GamelogLineParser(
            null!, NullLogger<GamelogLineParser>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_throws_on_null_logger()
    {
        Action act = () => _ = new GamelogLineParser(
            new StubSdeLookup(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_TickLine_LookupReceivesVerbatimName_NoStripping()
    {
        // Per MEMORY.md feedback_ore_is_just_ore: parser does NOT classify or
        // normalize. The captured name is forwarded verbatim (after .Trim()).
        // The rarity-tier suffix " III-Grade" stays attached.
        var (parser, sde, _) = BuildParser();

        parser.Parse(TickLine, FakeCharacterId);

        sde.NameLookups.Should().ContainSingle().Which.Should().Be("Nocxite III-Grade");
    }

    [Fact]
    public void Parse_AmountWithLeadingZeros_StillParses()
    {
        var (parser, _, _) = BuildParser();
        var line = TickLine.Replace(">32<", ">032<");

        var ev = parser.Parse(line, FakeCharacterId);

        ev.Should().NotBeNull();
        ev!.Amount.Should().Be(32L);
    }

    [Fact]
    public void Parse_TickLine_DifferentTimestamp_ParsesCorrectly()
    {
        // Spot-check: timestamp parsing isn't pinned to one literal value.
        var (parser, _, _) = BuildParser();
        var line = TickLine.Replace("2026.04.28 23:41:45", "2025.01.01 00:00:00");

        var ev = parser.Parse(line, FakeCharacterId);

        ev!.EveTimestamp.Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
