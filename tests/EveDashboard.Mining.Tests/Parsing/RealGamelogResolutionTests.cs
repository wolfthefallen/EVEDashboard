using EveDashboard.Core.Domain;
using EveDashboard.Mining.Parsing;
using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Lookup;
using EveDashboard.Sde.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Mining.Tests.Parsing;

/// <summary>
/// Integration test: every product name parsed from every fixture under
/// <c>tests/EveDashboard.Mining.Tests/testdata/Gamelogs/</c> must resolve through
/// <see cref="SqliteSdeLookup"/> backed by the shipped SDE fallback. Catches
/// real "EVE-name-not-in-our-SDE" gaps that hand-seeded unit tests cannot.
/// </summary>
public class RealGamelogResolutionTests
{
    /// <summary>
    /// Where the test fixtures live, relative to the test binary output directory.
    /// <c>EveDashboard.Mining.Tests.csproj</c> copies <c>testdata/**/*</c> with
    /// <c>PreserveNewest</c>, so <see cref="AppContext.BaseDirectory"/> contains
    /// the fixture tree at runtime.
    /// </summary>
    private static string FixturesRoot =>
        Path.Combine(AppContext.BaseDirectory, "testdata", "Gamelogs");

    [Fact]
    public async Task EveryProductMinedInFixtures_ResolvesThroughShippedSde()
    {
        // Extract the shipped SDE fallback into a temp file for this test run.
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "evemining-resolution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "eve.sqlite");

        try
        {
            var storage = new SdeStorage(NullLogger<SdeStorage>.Instance, dbPath);
            var extracted = await storage.EnsureDatabaseExistsAsync();
            extracted.Should().BeTrue("the temp directory was empty so the embedded fallback should be extracted");

            await using var db = new SqliteDatabase(
                $"Data Source={dbPath};Mode=ReadOnly",
                NullLogger<SqliteDatabase>.Instance);
            using var lookup = new SqliteSdeLookup(db, NullLogger<SqliteSdeLookup>.Instance);
            await lookup.LoadAsync();

            var parser = new GamelogLineParser(lookup, NullLogger<GamelogLineParser>.Instance);

            var fixtureFiles = Directory.GetFiles(FixturesRoot, "*.txt", SearchOption.AllDirectories);
            fixtureFiles.Should().NotBeEmpty("test fixtures must exist for the resolution test to be meaningful");

            // Walk every line in every fixture; parse; collect product names of
            // ticks and crits. Residue events carry no product name by design.
            var productNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in fixtureFiles)
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    var ev = parser.Parse(line, characterId: 0L);
                    if (ev is null) continue;
                    if (ev.Kind == MiningEventKind.Residue) continue;
                    productNames.Add(ev.ProductName);
                }
            }

            productNames.Should().NotBeEmpty(
                "fixtures must contain at least one mining tick or crit line");

            // The contract: every product name the user has actually mined in
            // real EVE play resolves through what we ship. Any miss is a real
            // SDE gap — fix the SDE / regenerate the fallback, not the test.
            foreach (var name in productNames)
            {
                lookup.TryGetTypeId(name).Should().NotBeNull(
                    $"the shipped SDE fallback must contain '{name}' (every product mined in fixtures resolves)");
            }
        }
        finally
        {
            // Best-effort cleanup of the per-test temp directory. Narrow catch
            // (only file-system races / lock contention); the test result is
            // already determined at this point so we do not surface failures
            // from cleanup, but we do not silently swallow either: any caught
            // exception is forwarded to the xUnit console for post-run inspection.
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Cleanup failed for {tempDir}: {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"Cleanup failed for {tempDir}: {ex}");
            }
        }
    }
}
