using System.Text;
using EveDashboard.Core.Domain;
using EveDashboard.Core.Lookup;
using EveDashboard.Core.Realtime;
using EveDashboard.Mining.Parsing;
using EveDashboard.Mining.Realtime;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EveDashboard.Mining.Tests.Realtime;

/// <summary>
/// Watcher tests - exercises the real-time tail pipeline against a
/// temp directory with synthetic gamelog files. Uses a real
/// <see cref="GamelogLineParser"/> over a fake <see cref="ISdeLookup"/>;
/// the parser shape itself is covered by the parser's own tests.
/// </summary>
public class RealtimeLogWatcherTests
{
    private const long PilotId = 2123642033;
    private const long UnknownPilotId = 9999999;
    private const long NocxiteIITypeId = 82017;

    /// <summary>A real Tick line shape lifted verbatim from the fixtures.</summary>
    private const string TickLine =
        "[ 2026.04.28 23:41:25 ] (mining) <color=0x77ffffff>You mined <font size=12>"
      + "<color=#ff8dc169>96<color=0x77ffffff><font size=10> units of "
      + "<color=0xffffffff><font size=12>Nocxite II-Grade";

    private const string GamelogHeader =
        "------------------------------------------------------------\r\n"
      + "  Gamelog\r\n"
      + "  Listener: TestPilot\r\n"
      + "  Session Started: 2026.04.28 22:49:20\r\n"
      + "------------------------------------------------------------\r\n";

    [Fact]
    public async Task Watcher_emits_event_when_new_line_is_appended()
    {
        await using var rig = await TestRig.CreateAsync();
        await rig.Watcher.StartAsync("sess-1", new[] { rig.Pilot }, CancellationToken.None);

        AppendBytes(rig.PilotLogPath, TickLine + "\r\n");
        await rig.Sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));

        rig.Sink.Events.Should().HaveCount(1);
        rig.Sink.Events[0].Kind.Should().Be(MiningEventKind.Tick);
        rig.Sink.Events[0].CharacterId.Should().Be(PilotId);
        rig.Sink.Events[0].Amount.Should().Be(96);
        rig.Sink.Events[0].TypeId.Should().Be(NocxiteIITypeId);
    }

    [Fact]
    public async Task Watcher_buffers_partial_line_until_newline_arrives()
    {
        await using var rig = await TestRig.CreateAsync();
        await rig.Watcher.StartAsync("sess-1", new[] { rig.Pilot }, CancellationToken.None);

        // Write the first half of the tick line WITHOUT a newline.
        var split = TickLine.Length / 2;
        var firstHalf = TickLine.Substring(0, split);
        var secondHalf = TickLine.Substring(split);
        AppendBytes(rig.PilotLogPath, firstHalf);
        await Task.Delay(300);
        rig.Sink.Events.Should().BeEmpty(
            "the buffered partial line is not yet terminated by '\\n'");

        // Append the rest plus a newline; the watcher must concatenate the
        // two halves and emit exactly one event.
        AppendBytes(rig.PilotLogPath, secondHalf + "\r\n");
        await rig.Sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        rig.Sink.Events.Should().HaveCount(1);
        rig.Sink.Events[0].Amount.Should().Be(96);
    }

    [Fact]
    public async Task Watcher_attaches_to_new_log_when_pilot_logs_back_in()
    {
        await using var rig = await TestRig.CreateAsync();
        await rig.Watcher.StartAsync("sess-1", new[] { rig.Pilot }, CancellationToken.None);

        // Pilot logs back into EVE; a new gamelog file is created with a
        // newer timestamp prefix for the same character id.
        var newFile = Path.Combine(rig.Directory, $"20260501_010101_{PilotId}.txt");
        File.WriteAllText(newFile, GamelogHeader);
        AppendBytes(newFile, TickLine + "\r\n");

        await rig.Sink.WaitForCountAsync(1, TimeSpan.FromSeconds(3));
        rig.Sink.Events.Should().HaveCount(1);
        rig.Sink.Events[0].CharacterId.Should().Be(PilotId);
    }

    [Fact]
    public async Task Watcher_ignores_files_for_pilots_not_in_session()
    {
        await using var rig = await TestRig.CreateAsync();
        await rig.Watcher.StartAsync("sess-1", new[] { rig.Pilot }, CancellationToken.None);

        // Brand-new gamelog for an UNKNOWN character id - watcher should
        // ignore Created event and never read the file.
        var unknownFile = Path.Combine(rig.Directory, $"20260501_020202_{UnknownPilotId}.txt");
        File.WriteAllText(unknownFile, GamelogHeader);
        AppendBytes(unknownFile, TickLine + "\r\n");

        await Task.Delay(800);
        rig.Sink.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Watcher_initial_position_is_EOF_for_existing_files()
    {
        await using var rig = await TestRig.CreateAsync();
        // Pre-existing tick line in the file BEFORE StartAsync.
        AppendBytes(rig.PilotLogPath, TickLine + "\r\n");

        await rig.Watcher.StartAsync("sess-1", new[] { rig.Pilot }, CancellationToken.None);
        await Task.Delay(500);
        rig.Sink.Events.Should().BeEmpty(
            "pre-existing log content is not replayed on Start");

        AppendBytes(rig.PilotLogPath, TickLine + "\r\n");
        await rig.Sink.WaitForCountAsync(1, TimeSpan.FromSeconds(2));
        rig.Sink.Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task StartAsync_called_twice_throws()
    {
        await using var rig = await TestRig.CreateAsync();
        await rig.Watcher.StartAsync("sess-1", new[] { rig.Pilot }, CancellationToken.None);

        Func<Task> act = () => rig.Watcher.StartAsync("sess-1", new[] { rig.Pilot }, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_throws_on_null_argument()
    {
        var sde = new FakeSdeLookup();
        var parser = new GamelogLineParser(sde, NullLogger<GamelogLineParser>.Instance);
        var sink = new CapturingSink();
        var log = NullLogger<RealtimeLogWatcher>.Instance;

        ((Action)(() => _ = new RealtimeLogWatcher(null!, sink, log))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new RealtimeLogWatcher(parser, null!, log))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new RealtimeLogWatcher(parser, sink, null!))).Should().Throw<ArgumentNullException>();
    }

    private static void AppendBytes(string path, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(flushToDisk: true);
    }

    private sealed class TestRig : IAsyncDisposable
    {
        public string Directory { get; }
        public string PilotLogPath { get; }
        public Character Pilot { get; }
        public RealtimeLogWatcher Watcher { get; }
        public CapturingSink Sink { get; }

        private TestRig(string dir, string pilotLogPath, Character pilot, RealtimeLogWatcher watcher, CapturingSink sink)
        {
            Directory = dir;
            PilotLogPath = pilotLogPath;
            Pilot = pilot;
            Watcher = watcher;
            Sink = sink;
        }

        public static Task<TestRig> CreateAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "EveDashboardWatcherTests-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var pilotLogPath = Path.Combine(dir, $"20260428_224920_{PilotId}.txt");
            File.WriteAllText(pilotLogPath, GamelogHeader);

            var pilot = new Character(PilotId, "TestPilot", pilotLogPath);
            var sde = new FakeSdeLookup();
            var parser = new GamelogLineParser(sde, NullLogger<GamelogLineParser>.Instance);
            var sink = new CapturingSink();
            var watcher = new RealtimeLogWatcher(parser, sink, NullLogger<RealtimeLogWatcher>.Instance);
            return Task.FromResult(new TestRig(dir, pilotLogPath, pilot, watcher, sink));
        }

        public async ValueTask DisposeAsync()
        {
            await Watcher.DisposeAsync();
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    private sealed class CapturingSink : IMiningEventSink
    {
        private readonly List<MiningEvent> _events = new();
        private readonly object _gate = new();

        public IReadOnlyList<MiningEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return _events.ToList();
                }
            }
        }

        public Task WriteAsync(MiningEvent ev, CancellationToken ct)
        {
            lock (_gate) { _events.Add(ev); }
            return Task.CompletedTask;
        }

        public async Task WaitForCountAsync(int expected, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                int n;
                lock (_gate) { n = _events.Count; }
                if (n >= expected) return;
                await Task.Delay(50);
            }
        }
    }

    /// <summary>
    /// Fake SDE lookup: resolves "Nocxite II-Grade" to the real type id; null
    /// otherwise. The parser's own tests cover the parser-SDE integration with
    /// real fixtures; the watcher tests just need a known mapping.
    /// </summary>
    private sealed class FakeSdeLookup : ISdeLookup
    {
        public long? TryGetTypeId(string name) =>
            string.Equals(name, "Nocxite II-Grade", StringComparison.OrdinalIgnoreCase) ? NocxiteIITypeId : null;

        public MiningProduct? TryGetProduct(long typeId) =>
            typeId == NocxiteIITypeId
                ? new MiningProduct(NocxiteIITypeId, "Nocxite II-Grade", VolumePerUnitM3: 1.0, PortionSize: 100)
                : null;

        public IReadOnlyList<TypeMaterial> GetTypeMaterials(long typeId) => Array.Empty<TypeMaterial>();
        public IReadOnlyList<long> GetRefinedMaterialTypeIds() => Array.Empty<long>();
        public string? TryGetRefinedMaterialName(long typeId) => null;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
