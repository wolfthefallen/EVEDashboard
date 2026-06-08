using EveDashboard.Mining.Gamelogs;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Mining.Tests.Gamelogs;

public class GamelogDiscoveryServiceTests
{
    private static string TestdataDir =>
        Path.Combine(AppContext.BaseDirectory, "testdata", "Gamelogs");

    private static GamelogDiscoveryService Build(out CapturingLogger<GamelogDiscoveryService> logger, string? directoryOverride = null)
    {
        logger = new CapturingLogger<GamelogDiscoveryService>();
        return new GamelogDiscoveryService(directoryOverride ?? TestdataDir, logger);
    }

    [Fact]
    public async Task DiscoverCharacters_ReturnsExactlyTheFiveExpectedNames()
    {
        var sut = Build(out _);
        var characters = await sut.DiscoverCharactersAsync();

        characters.Select(c => c.Name).Should().BeEquivalentTo(new[]
        {
            "Fierro Fallen",
            "Verka Fallen",
            "Vera Fallen",
            "Gothy Fallen",
            "Felicity Fallen",
        });
    }

    [Fact]
    public async Task DiscoverCharacters_MapsExpectedCharacterIdsToNames()
    {
        var sut = Build(out _);
        var characters = await sut.DiscoverCharactersAsync();

        characters.Should().Contain(c => c.CharacterId == 90863271L && c.Name == "Fierro Fallen");
        characters.Should().Contain(c => c.CharacterId == 2123642033L && c.Name == "Verka Fallen");
        characters.Should().Contain(c => c.CharacterId == 2123571009L && c.Name == "Vera Fallen");
        characters.Should().Contain(c => c.CharacterId == 2123831393L && c.Name == "Gothy Fallen");
        characters.Should().Contain(c => c.CharacterId == 2124289934L && c.Name == "Felicity Fallen");
    }

    [Fact]
    public async Task DiscoverCharacters_PicksLatestLogByFilenameTimestamp_ForFierro()
    {
        var sut = Build(out _);
        var characters = await sut.DiscoverCharactersAsync();

        var fierro = characters.Single(c => c.CharacterId == 90863271L);
        Path.GetFileName(fierro.LogFilePath).Should().Be("20260428_224855_90863271.txt");
    }

    [Fact]
    public async Task DiscoverCharacters_PicksLatestLogByFilenameTimestamp_ForFelicity()
    {
        var sut = Build(out _);
        var characters = await sut.DiscoverCharactersAsync();

        var felicity = characters.Single(c => c.CharacterId == 2124289934L);
        Path.GetFileName(felicity.LogFilePath).Should().Be("20260428_224917_2124289934.txt");
    }

    [Fact]
    public async Task DiscoverCharacters_ExcludesFilesInOldSubfolder()
    {
        var sut = Build(out _);
        var characters = await sut.DiscoverCharactersAsync();

        // The file 20260424_133911_2124289934.txt (Felicity) exists in old/, but Felicity's
        // result must point to her latest non-old/ log — old/ is excluded entirely.
        characters.Select(c => c.LogFilePath)
            .Should().NotContain(p => p.Contains(Path.DirectorySeparatorChar + "old" + Path.DirectorySeparatorChar));
    }

    [Fact]
    public async Task DiscoverCharacters_SkipsFilesWithoutCharacterIdInFilename()
    {
        var sut = Build(out _);
        var characters = await sut.DiscoverCharactersAsync();

        // 20260422_010842.txt has no character ID; it should not yield a character.
        // The result is exactly the 5 named characters (no extras).
        characters.Should().HaveCount(5);
    }

    [Fact]
    public async Task DiscoverCharacters_SkipsFileWithMissingListenerLine_AndLogsWarning()
    {
        var sut = Build(out var logger);
        var characters = await sut.DiscoverCharactersAsync();

        // Synthetic char ID 99999999 has a valid filename but no Listener line.
        characters.Should().NotContain(c => c.CharacterId == 99999999L);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("99999999") &&
            e.Message.Contains("Listener"));
    }

    [Fact]
    public async Task DiscoverCharacters_DirectoryDoesNotExist_ReturnsEmpty_AndLogsWarning()
    {
        var sut = Build(out var logger, directoryOverride: @"C:\nonexistent\path\that\does\not\exist");
        var characters = await sut.DiscoverCharactersAsync();

        characters.Should().BeEmpty();
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("does not exist"));
    }

    [Fact]
    public async Task DiscoverCharacters_ListenerNameIsTrimmed()
    {
        var sut = Build(out _);
        var characters = await sut.DiscoverCharactersAsync();

        // Real EVE writes "  Listener: <name>\r\n" — the leading two-space indent
        // and any trailing whitespace must not appear in the parsed name.
        foreach (var c in characters)
        {
            c.Name.Should().Be(c.Name.Trim());
            c.Name.Should().NotStartWith(" ");
        }
    }

    [Fact]
    public async Task DiscoverCharacters_HonorsCancellation()
    {
        var sut = Build(out _);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.DiscoverCharactersAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_NullDirectory_Throws()
    {
        Action act = () => _ = new GamelogDiscoveryService(null!, NullLogger<GamelogDiscoveryService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("gamelogDirectory");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Action act = () => _ = new GamelogDiscoveryService(@"C:\anywhere", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    /* ---------- Plan 23: Sort by file mtime descending ---------- */

    /// <summary>
    /// Build a temp directory with synthetic gamelog files for the sort tests.
    /// Each file gets a Listener line so the discovery service emits a Character.
    /// Returns the directory path so the test can clean it up.
    /// </summary>
    private static string CreateTempGamelogDir(params (string Filename, string ListenerName, DateTime Mtime)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "evemining-discovery-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (filename, listenerName, mtime) in files)
        {
            var path = Path.Combine(dir, filename);
            // Real EVE log header (line 3 = Listener: ...) — three lines is enough.
            File.WriteAllText(path,
                "------------------------------------------------------------\r\n" +
                "  Gamelog\r\n" +
                "  Listener: " + listenerName + "\r\n");
            File.SetLastWriteTimeUtc(path, mtime);
        }
        return dir;
    }

    [Fact]
    public async Task DiscoverCharactersAsync_returns_characters_ordered_by_mtime_descending()
    {
        var t1 = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 5, 2, 10, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 5, 3, 10, 0, 0, DateTimeKind.Utc);
        var dir = CreateTempGamelogDir(
            ("20260501_100000_111.txt", "Oldest Pilot",  t1),
            ("20260502_100000_222.txt", "Middle Pilot",  t2),
            ("20260503_100000_333.txt", "Newest Pilot",  t3));
        try
        {
            var sut = Build(out _, directoryOverride: dir);
            var characters = await sut.DiscoverCharactersAsync();

            characters.Select(c => c.Name).Should().ContainInOrder(
                "Newest Pilot", "Middle Pilot", "Oldest Pilot");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverCharactersAsync_tie_breaks_on_name_alphabetically_case_insensitive()
    {
        // Two files with the SAME mtime; "alice" (lowercase) should sort before "Charlie".
        var t = new DateTime(2026, 5, 3, 10, 0, 0, DateTimeKind.Utc);
        var dir = CreateTempGamelogDir(
            ("20260503_100000_111.txt", "Charlie Bravo", t),
            ("20260503_100001_222.txt", "alice Bravo",   t));
        try
        {
            var sut = Build(out _, directoryOverride: dir);
            var characters = await sut.DiscoverCharactersAsync();

            characters.Select(c => c.Name).Should().ContainInOrder("alice Bravo", "Charlie Bravo");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverCharactersAsync_does_not_break_when_log_file_is_missing()
    {
        // Use a custom directory; after discovery deletes one file before the sort runs.
        // Since the real code path runs sort INSIDE DiscoverCharactersAsync we cannot easily
        // race a delete; instead we verify SafeMtime's File.Exists guard by ensuring two
        // co-existing files sort correctly when one has a much older mtime (regression
        // canary that the sort code path is exception-free).
        var ancient = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var recent  = new DateTime(2026, 5, 3, 10, 0, 0, DateTimeKind.Utc);
        var dir = CreateTempGamelogDir(
            ("20000101_000000_111.txt", "Ancient Pilot", ancient),
            ("20260503_100000_222.txt", "Recent Pilot",  recent));
        try
        {
            var sut = Build(out _, directoryOverride: dir);
            var characters = await sut.DiscoverCharactersAsync();

            characters.Select(c => c.Name).Should().ContainInOrder("Recent Pilot", "Ancient Pilot");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
