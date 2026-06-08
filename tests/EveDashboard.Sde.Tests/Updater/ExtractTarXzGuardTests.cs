namespace EveDashboard.Sde.Tests.Updater;

using System.Formats.Tar;
using EveDashboard.Core.Storage;
using EveDashboard.Persistence.Storage;
using EveDashboard.Sde.Conversion;
using EveDashboard.Sde.Storage;
using EveDashboard.Sde.Updater;
using EveDashboard.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpCompress.Common;

/// <summary>
/// Defense-in-depth tests for <see cref="EverefSdeUpdater.ExtractTarXz"/>'s
/// traversal + symlink guards (GHSA-6c8g-7p36-r338 / CWE-22).
/// </summary>
/// <remarks>
/// Crafts in-process TAR archives via <see cref="System.Formats.Tar.TarWriter"/>
/// (no external tooling, no shipped binary fixtures), then calls the internal
/// <c>ExtractTarXz</c> directly and asserts the guard rejects each attack
/// vector while still extracting benign entries cleanly. Each test isolates
/// itself in a per-test temp directory and verifies that no file or directory
/// has been written outside the extraction root.
/// </remarks>
public sealed class ExtractTarXzGuardTests : IDisposable
{
    private readonly string _scratchRoot;
    private readonly string _extractDir;
    private readonly CapturingLogger<EverefSdeUpdater> _log;
    private readonly EverefSdeUpdater _updater;

    /// <summary>Build the per-test scratch directory + a minimal updater wired with no-op deps.</summary>
    public ExtractTarXzGuardTests()
    {
        _scratchRoot = Path.Combine(
            Path.GetTempPath(), "evemining-sde-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchRoot);
        _extractDir = Path.Combine(_scratchRoot, "dest");
        Directory.CreateDirectory(_extractDir);

        // ExtractTarXz only needs `_log` from the updater (the other deps are
        // unused on this code path). Pass concrete-but-unused stubs for the
        // rest so the ctor's null-checks pass.
        _log = new CapturingLogger<EverefSdeUpdater>();
        _updater = new EverefSdeUpdater(
            http:      new HttpClient(),
            storage:   new StubSdeStorage(Path.Combine(_scratchRoot, "ignored.sqlite")),
            db:        new SqliteDatabase(
                "Data Source=:memory:", NullLogger<SqliteDatabase>.Instance),
            converter: new EverefSdeFormatConverter(
                NullLogger<EverefSdeFormatConverter>.Instance),
            log:       _log,
            minTypeRowsForCommit: 1);
    }

    /// <summary>Clean up the per-test scratch directory.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_scratchRoot, recursive: true); }
        catch (IOException)                { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }

    [Fact]
    public void Rejects_entry_with_relative_traversal_key()
    {
        var tarPath = BuildTar(
            new TarFileEntry("../escape.json", "evil"));

        var act = () => _updater.ExtractTarXz(tarPath, _extractDir);

        act.Should().ThrowExactly<ExtractionException>()
            .WithMessage("*outside the extraction root*GHSA-6c8g-7p36-r338*");
        AssertNothingEscapedExtractionRoot();
    }

    [Fact]
    public void Rejects_entry_with_parent_sibling_prefix_collision_key()
    {
        // Sibling-prefix attack: extract root is .../dest, entry tries to escape
        // into .../dest-evil/... which textually starts with the same prefix.
        var tarPath = BuildTar(
            new TarFileEntry("../dest-evil/escape.json", "evil"));

        var act = () => _updater.ExtractTarXz(tarPath, _extractDir);

        act.Should().ThrowExactly<ExtractionException>()
            .WithMessage("*outside the extraction root*");
        AssertNothingEscapedExtractionRoot();
    }

    [Fact]
    public void Skips_symlink_entries_with_warning_log()
    {
        var tarPath = BuildTar(
            new TarSymlinkEntry("link", "../escape_target"));

        var act = () => _updater.ExtractTarXz(tarPath, _extractDir);

        act.Should().NotThrow();
        Directory.GetFileSystemEntries(_extractDir).Should().BeEmpty(
            "symlink entries must not produce any filesystem artefact under the extraction root");
        _log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("symlink entry"));
        AssertNothingEscapedExtractionRoot();
    }

    [Fact]
    public void Extracts_benign_entry_normally()
    {
        var tarPath = BuildTar(
            new TarFileEntry("normal.json", "{\"ok\":1}"));

        var act = () => _updater.ExtractTarXz(tarPath, _extractDir);

        act.Should().NotThrow();
        File.Exists(Path.Combine(_extractDir, "normal.json")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_extractDir, "normal.json")).Should().Be("{\"ok\":1}");
    }

    // ---- helpers -----------------------------------------------------------

    private string BuildTar(params TarTestEntry[] entries)
    {
        var tarPath = Path.Combine(_scratchRoot, "test-" + Guid.NewGuid().ToString("N") + ".tar");
        using (var fs = File.Create(tarPath))
        using (var writer = new TarWriter(fs, TarEntryFormat.Ustar, leaveOpen: false))
        {
            foreach (var e in entries)
            {
                writer.WriteEntry(e.ToTarEntry());
            }
        }
        return tarPath;
    }

    /// <summary>Assert no file or directory exists under <see cref="_scratchRoot"/> outside <see cref="_extractDir"/> other than the source TAR(s).</summary>
    private void AssertNothingEscapedExtractionRoot()
    {
        var unexpected = Directory.EnumerateFileSystemEntries(
                _scratchRoot, "*", SearchOption.AllDirectories)
            .Where(p =>
                !p.StartsWith(_extractDir, StringComparison.Ordinal)   // anything under dest/ is fine
                && !p.EndsWith(".tar", StringComparison.Ordinal)        // source TARs are fine
                && p != _extractDir)
            .ToList();
        unexpected.Should().BeEmpty(
            "no entry should ever produce a filesystem artefact outside the extraction root");
    }

    private abstract record TarTestEntry
    {
        public abstract TarEntry ToTarEntry();
    }

    private sealed record TarFileEntry(string Name, string Payload) : TarTestEntry
    {
        public override TarEntry ToTarEntry()
        {
            var entry = new UstarTarEntry(TarEntryType.RegularFile, Name);
            var bytes = System.Text.Encoding.UTF8.GetBytes(Payload);
            entry.DataStream = new MemoryStream(bytes);
            return entry;
        }
    }

    private sealed record TarSymlinkEntry(string Name, string LinkTarget) : TarTestEntry
    {
        public override TarEntry ToTarEntry()
        {
            return new UstarTarEntry(TarEntryType.SymbolicLink, Name)
            {
                LinkName = LinkTarget,
            };
        }
    }

    private sealed class StubSdeStorage : ISdeStorage
    {
        public StubSdeStorage(string path) { DatabasePath = path; }
        public string DatabasePath { get; }
        public Task<bool> EnsureDatabaseExistsAsync(CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}
