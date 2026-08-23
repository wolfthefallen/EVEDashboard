using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Conversion;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EveDashboard.Sde.Tests.Conversion;

/// <summary>
/// Manual-only test that builds the shipped fallback
/// SQLite from a freshly-extracted everef.net JSON folder. Skipped under default
/// <c>dotnet test</c> runs.
/// </summary>
/// <remarks>
/// To run:
/// <list type="number">
///   <item>Download <c>https://data.everef.net/reference-data/reference-data-latest.tar.xz</c></item>
///   <item>Extract to a folder containing <c>meta.json</c>, <c>categories.json</c>, <c>groups.json</c>, <c>types.json</c></item>
///   <item>Set environment variable <c>EVE_SDE_JSON_PATH</c> to that folder</item>
///   <item>Comment out the <c>Skip = "..."</c> argument on the <c>[Fact]</c> below</item>
///   <item><c>dotnet test tests/EveDashboard.Sde.Tests --filter "FullyQualifiedName~FallbackBuildTests"</c></item>
///   <item>Re-add the <c>Skip</c> argument before committing</item>
/// </list>
/// Output is written to <c>src/EveDashboard.Sde/Data/sde-fallback.sqlite</c>
/// (relative to the repo root, computed dynamically).
/// </remarks>
public class FallbackBuildTests
{
    [Fact(Skip = "Run manually to regenerate the fallback SDE; the class XML doc describes the procedure.")]
    public async Task RegenerateFallback()
    {
        var jsonPath = Environment.GetEnvironmentVariable("EVE_SDE_JSON_PATH")
            ?? throw new InvalidOperationException(
                "Set EVE_SDE_JSON_PATH to the folder containing extracted everef.net JSON files.");

        var repoRoot = FindRepoRoot();
        var outputPath = Path.Combine(repoRoot, "src", "EveDashboard.Sde", "Data", "sde-fallback.sqlite");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }
        SqliteConnection.ClearAllPools();

        var converter = new EverefSdeFormatConverter(NullLogger<EverefSdeFormatConverter>.Instance);
        var progressLog = new List<ImportProgress>();
        var progress = new Progress<ImportProgress>(progressLog.Add);

        await using (var db = new SqliteDatabase($"Data Source={outputPath}", NullLogger<SqliteDatabase>.Instance))
        {
            await converter.ImportAsync(jsonPath, db, progress);
        }
        SqliteConnection.ClearAllPools();

        // Sanity-check the output.
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Exists.Should().BeTrue();
        fileInfo.Length.Should().BeGreaterThan(1_000_000, "fallback should be at least 1 MB");
        fileInfo.Length.Should().BeLessThan(50_000_000, "fallback should be under 50 MB");

        await using var verifyDb = new SqliteDatabase($"Data Source={outputPath}", NullLogger<SqliteDatabase>.Instance);
        var typeCount = await verifyDb.QueryScalarAsync<long>("SELECT COUNT(*) FROM types");
        typeCount.Should().BeGreaterThan(10_000, "expect ~25-35K published types in real EVE SDE");

        var groupCount = await verifyDb.QueryScalarAsync<long>("SELECT COUNT(*) FROM groups");
        groupCount.Should().BeGreaterThan(100, "expect ~1500 published groups");

        var oreCount = await verifyDb.QueryScalarAsync<long>("SELECT COUNT(*) FROM types WHERE is_ore = 1");
        oreCount.Should().BeGreaterThan(50, "expect plenty of mineable ore types");

        // Print build stats so whoever regenerates the fallback can record them.
        Console.WriteLine($"=== Fallback build stats ===");
        Console.WriteLine($"  File:           {outputPath}");
        Console.WriteLine($"  File size:      {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0 / 1024.0:F2} MB)");
        Console.WriteLine($"  Categories:     {await verifyDb.QueryScalarAsync<long>("SELECT COUNT(*) FROM categories"):N0}");
        Console.WriteLine($"  Groups:         {groupCount:N0}");
        Console.WriteLine($"  Types:          {typeCount:N0}");
        Console.WriteLine($"  type_materials: {await verifyDb.QueryScalarAsync<long>("SELECT COUNT(*) FROM type_materials"):N0}");
        Console.WriteLine($"  is_ore types:   {oreCount:N0}");
        Console.WriteLine($"  Build time:     {await verifyDb.QueryScalarAsync<string>("SELECT build_time FROM meta WHERE id = 1")}");
        Console.WriteLine($"  Progress events captured: {progressLog.Count}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EveDashboard.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repo root (looked for EveDashboard.sln)");
    }
}
