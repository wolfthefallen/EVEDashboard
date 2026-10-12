using EveDashboard.Mining.Historical;
using EveDashboard.Mining.Parsing;
using EveDashboard.Mining.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Mining.Tests.Historical;

/// <summary>
/// Exercises <see cref="HistoricalGamelogReader.InventoryAsync"/> against the
/// committed real gamelogs under <c>testdata/Gamelogs/</c>. Known fixture facts:
/// six pilots have id-bearing filenames - <c>90863271</c> (Fierro Fallen, 2
/// files), <c>2124289934</c> (Felicity Fallen, 3 files, the oldest being
/// <c>old\20260424_133911</c>), <c>2123571009</c> (Vera Fallen),
/// <c>2123831393</c> (Gothy Fallen), <c>2123642033</c> (Verka Fallen), and
/// <c>99999999</c>, whose <c>20240101_000000</c> file has no <c>Listener:</c>
/// header. <c>20260422_010842.txt</c> carries no character id and is ignored.
/// </summary>
public class GamelogInventoryTests
{
    private const long Fierro = 90863271L;
    private const long Felicity = 2124289934L;
    private const long NoHeaderPilot = 99999999L;

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
    public async Task InventoryAsync_lists_every_pilot_with_an_id_bearing_gamelog()
    {
        var inventory = await NewReader().InventoryAsync();

        inventory.Pilots.Select(p => p.PilotId).Should().Equal(
            Fierro, NoHeaderPilot, 2123571009L, 2123642033L, 2123831393L, Felicity);
        inventory.Pilots.Select(p => p.PilotId).Should().BeInAscendingOrder(
            "the inventory is ordered by pilot id so the result is deterministic");
    }

    [Fact]
    public async Task InventoryAsync_names_pilots_from_the_listener_header()
    {
        var inventory = await NewReader().InventoryAsync();

        inventory.Pilots.Single(p => p.PilotId == Fierro).Name.Should().Be("Fierro Fallen");
        inventory.Pilots.Single(p => p.PilotId == Felicity).Name.Should().Be("Felicity Fallen");
        inventory.Pilots.Single(p => p.PilotId == 2123571009L).Name.Should().Be("Vera Fallen");
    }

    [Fact]
    public async Task InventoryAsync_falls_back_to_the_id_when_no_listener_header_can_be_read()
    {
        var inventory = await NewReader().InventoryAsync();

        inventory.Pilots.Single(p => p.PilotId == NoHeaderPilot).Name.Should().Be("99999999");
    }

    [Fact]
    public async Task InventoryAsync_includes_the_old_subfolder_in_a_pilots_history()
    {
        var inventory = await NewReader().InventoryAsync();

        var felicity = inventory.Pilots.Single(p => p.PilotId == Felicity);
        felicity.FileCount.Should().Be(3);
        felicity.FirstLogStart.Should().Be(
            Utc(2026, 4, 24, 13, 39, 11),
            "the oldest log lives under old\\ and must still count as history");
    }

    [Fact]
    public async Task InventoryAsync_takes_each_pilots_first_log_from_their_oldest_file()
    {
        var inventory = await NewReader().InventoryAsync();

        var fierro = inventory.Pilots.Single(p => p.PilotId == Fierro);
        fierro.FileCount.Should().Be(2);
        fierro.FirstLogStart.Should().Be(Utc(2026, 4, 28, 21, 17, 13));
    }

    [Fact]
    public async Task InventoryAsync_reports_the_overall_span_of_the_folder()
    {
        var inventory = await NewReader().InventoryAsync();

        inventory.EarliestLogStart.Should().Be(
            Utc(2024, 1, 1, 0, 0, 0), "that is the oldest filename timestamp in the folder");
        inventory.EarliestLogStart.Should().Be(inventory.Pilots.Min(p => p.FirstLogStart));
        inventory.LatestActivity.Should().Be(inventory.Pilots.Max(p => p.LastActivity));
        inventory.LatestActivity.Should().BeOnOrAfter(inventory.EarliestLogStart!.Value);
    }

    [Fact]
    public async Task InventoryAsync_returns_an_empty_inventory_for_a_missing_directory()
    {
        var reader = NewReader(Path.Combine(Path.GetTempPath(), "eve-dashboard-no-such-folder-" + Guid.NewGuid().ToString("N")));

        var inventory = await reader.InventoryAsync();

        inventory.Pilots.Should().BeEmpty();
        inventory.EarliestLogStart.Should().BeNull();
        inventory.LatestActivity.Should().BeNull();
    }

    [Fact]
    public async Task InventoryAsync_returns_an_empty_inventory_for_a_folder_without_gamelogs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "eve-dashboard-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "notes.txt"), "not a gamelog");

            var inventory = await NewReader(dir).InventoryAsync();

            inventory.Pilots.Should().BeEmpty();
            inventory.EarliestLogStart.Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task InventoryAsync_honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => NewReader().InventoryAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InventoryAsync_does_not_disturb_ReadAsync()
    {
        var reader = NewReader();

        await reader.InventoryAsync();
        var result = await reader.ReadAsync(new[] { Fierro }, Utc(2026, 4, 28, 23, 0), Utc(2026, 4, 29, 2, 0));

        result.Events.Should().HaveCount(526, "the shared enumeration must not change what a window read returns");
    }
}
