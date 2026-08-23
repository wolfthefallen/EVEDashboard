using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Mining.Historical;
using EveDashboard.Mining.Parsing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Mining.Tests.Historical;

/// <summary>
/// Exercises <see cref="HistoricalGamelogReader"/> against the committed real
/// gamelogs under <c>testdata/Gamelogs/</c>. Known fixture facts: pilot
/// <c>90863271</c> has <c>20260428_211713</c> (no mining lines) and
/// <c>20260428_224855</c> (526 mining lines, 2026-04-28 23:41:45 .. 2026-04-29
/// 01:16:19); pilot <c>2124289934</c> has <c>20260424_205954</c> (none),
/// <c>20260428_224917</c> (431 mining lines) and <c>old\20260424_133911</c> (none).
/// </summary>
public class HistoricalGamelogReaderTests
{
    private const long PilotA = 90863271L;
    private const long PilotB = 2124289934L;

    private static string FixturesRoot =>
        Path.Combine(AppContext.BaseDirectory, "testdata", "Gamelogs");

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi, int s = 0) =>
        new(y, mo, d, h, mi, s, TimeSpan.Zero);

    private static HistoricalGamelogReader NewReader(string? directory = null) =>
        new(
            directory ?? FixturesRoot,
            new GamelogLineParser(new AnyOreLookup(), NullLogger<GamelogLineParser>.Instance),
            NullLogger<HistoricalGamelogReader>.Instance);

    [Fact]
    public async Task ReadAsync_returns_every_event_inside_a_window_that_covers_one_file()
    {
        var reader = NewReader();

        var result = await reader.ReadAsync(new[] { PilotA }, Utc(2026, 4, 28, 23, 0), Utc(2026, 4, 29, 2, 0));

        result.Events.Should().HaveCount(526);
        result.Events.Should().OnlyContain(e => e.CharacterId == PilotA);
        result.Events.First().EveTimestamp.Should().Be(Utc(2026, 4, 28, 23, 41, 45));
        result.Events.Last().EveTimestamp.Should().Be(Utc(2026, 4, 29, 1, 16, 19));
        result.PilotsWithoutLogs.Should().BeEmpty();
        // The earlier 21:17:13 file was superseded by the 22:48:55 file before
        // the window began, so only the newer file is opened.
        result.FilesRead.Should().ContainSingle()
            .Which.Should().EndWith("20260428_224855_90863271.txt");
    }

    [Fact]
    public async Task ReadAsync_filters_by_the_per_line_timestamp_not_the_file()
    {
        var reader = NewReader();

        var result = await reader.ReadAsync(new[] { PilotA }, Utc(2026, 4, 28, 23, 41, 45), Utc(2026, 4, 28, 23, 59, 59));

        result.Events.Should().NotBeEmpty();
        result.Events.Count.Should().BeLessThan(526);
        result.Events.Should().OnlyContain(e =>
            e.EveTimestamp >= Utc(2026, 4, 28, 23, 41, 45) && e.EveTimestamp <= Utc(2026, 4, 28, 23, 59, 59));
        result.Events.First().EveTimestamp.Should().Be(Utc(2026, 4, 28, 23, 41, 45), "the window start is inclusive");
    }

    [Fact]
    public async Task ReadAsync_merges_several_pilots_sorted_by_timestamp()
    {
        var reader = NewReader();

        var result = await reader.ReadAsync(new[] { PilotA, PilotB }, Utc(2026, 4, 28, 22, 0), Utc(2026, 4, 29, 3, 0));

        result.Events.Should().HaveCount(526 + 431);
        result.Events.Select(e => e.EveTimestamp).Should().BeInAscendingOrder();
        result.Events.Count(e => e.CharacterId == PilotA).Should().Be(526);
        result.Events.Count(e => e.CharacterId == PilotB).Should().Be(431);
        result.FilesRead.Should().Contain(p => p.EndsWith("20260428_224855_90863271.txt", StringComparison.Ordinal));
        result.FilesRead.Should().Contain(p => p.EndsWith("20260428_224917_2124289934.txt", StringComparison.Ordinal));
        result.PilotsWithoutLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_reports_pilots_that_have_no_gamelog_instead_of_throwing()
    {
        var reader = NewReader();

        var result = await reader.ReadAsync(new[] { 12345L, PilotA }, Utc(2026, 4, 28, 23, 0), Utc(2026, 4, 29, 2, 0));

        result.PilotsWithoutLogs.Should().Equal(12345L);
        result.Events.Should().HaveCount(526);
    }

    [Fact]
    public async Task ReadAsync_reads_files_from_the_old_subfolder()
    {
        var reader = NewReader();

        // Only the archived 2026-04-24 13:39:11 file can overlap this window: the
        // 20:59:54 file starts after the window stops, and the 04-28 file later still.
        var result = await reader.ReadAsync(new[] { PilotB }, Utc(2026, 4, 24, 13, 0), Utc(2026, 4, 24, 20, 0));

        result.FilesRead.Should().ContainSingle()
            .Which.Should().EndWith(Path.Combine("old", "20260424_133911_2124289934.txt"));
        result.PilotsWithoutLogs.Should().BeEmpty("a file was found for the pilot even though it held no mining lines");
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_prunes_files_that_cannot_overlap_the_window()
    {
        var reader = NewReader();

        // A window entirely before any of pilot B's files: nothing is opened and
        // the pilot is reported as having no usable log.
        var result = await reader.ReadAsync(new[] { PilotB }, Utc(2026, 1, 1, 0, 0), Utc(2026, 1, 2, 0, 0));

        result.FilesRead.Should().BeEmpty();
        result.Events.Should().BeEmpty();
        result.PilotsWithoutLogs.Should().Equal(PilotB);
    }

    [Fact]
    public async Task ReadAsync_reads_a_filename_present_at_top_level_and_in_old_only_once()
    {
        var temp = Path.Combine(Path.GetTempPath(), "EveDashboard.Mining.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "old"));
        try
        {
            const string name = "20260428_224855_90863271.txt";
            File.Copy(Path.Combine(FixturesRoot, name), Path.Combine(temp, name));
            File.Copy(Path.Combine(FixturesRoot, name), Path.Combine(temp, "old", name));
            var reader = NewReader(temp);

            var result = await reader.ReadAsync(new[] { PilotA }, Utc(2026, 4, 28, 23, 0), Utc(2026, 4, 29, 2, 0));

            result.Events.Should().HaveCount(526, "the duplicate under old\\ must not double-count");
            result.FilesRead.Should().ContainSingle().Which.Should().Be(Path.Combine(temp, name), "the top-level copy wins");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task ReadAsync_on_a_missing_directory_returns_empty_and_flags_every_pilot()
    {
        var reader = NewReader(Path.Combine(Path.GetTempPath(), "EveDashboard.Mining.Tests", "does-not-exist-" + Guid.NewGuid().ToString("N")));

        var result = await reader.ReadAsync(new[] { PilotA, PilotB }, Utc(2026, 4, 28, 0, 0), Utc(2026, 4, 29, 0, 0));

        result.Events.Should().BeEmpty();
        result.FilesRead.Should().BeEmpty();
        result.PilotsWithoutLogs.Should().Equal(PilotA, PilotB);
    }

    [Fact]
    public async Task ReadAsync_rejects_a_window_whose_stop_precedes_its_start()
    {
        var reader = NewReader();

        var act = async () => await reader.ReadAsync(new[] { PilotA }, Utc(2026, 4, 29, 0, 0), Utc(2026, 4, 28, 0, 0));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadAsync_rejects_null_pilot_ids()
    {
        var reader = NewReader();

        var act = async () => await reader.ReadAsync(null!, Utc(2026, 4, 28, 0, 0), Utc(2026, 4, 29, 0, 0));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_nulls()
    {
        var parser = new GamelogLineParser(new AnyOreLookup(), NullLogger<GamelogLineParser>.Instance);

        var a = () => new HistoricalGamelogReader(null!, parser, NullLogger<HistoricalGamelogReader>.Instance);
        var b = () => new HistoricalGamelogReader(FixturesRoot, null!, NullLogger<HistoricalGamelogReader>.Instance);
        var c = () => new HistoricalGamelogReader(FixturesRoot, parser, null!);

        a.Should().Throw<ArgumentNullException>();
        b.Should().Throw<ArgumentNullException>();
        c.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Minimal <see cref="ISdeLookup"/>: resolves every product name to one type
    /// id so the real parser emits a Tick/Crit for every mining line. The reader
    /// under test only cares about timestamps and counts, not ore identity.
    /// </summary>
    private sealed class AnyOreLookup : ISdeLookup
    {
        public long? TryGetTypeId(string productName) => 1230L;
        public MiningProduct? TryGetProduct(long typeId) => null;
        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public string? TryGetRefinedMaterialName(long typeId) => null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
