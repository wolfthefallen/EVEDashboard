namespace EveDashboard.Sde.Tests.Updater;

using EveDashboard.Sde.Updater;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for <see cref="EverefSdeUpdater.CopyWithInactivityTimeoutAsync"/> — the
/// wall-clock guard that stops a stalled everef.net download from hanging startup
/// forever (the body transfer opts out of <c>HttpClient.Timeout</c> via
/// <c>ResponseHeadersRead</c> and production passes <c>CancellationToken.None</c>).
/// </summary>
public sealed class CopyWithInactivityTimeoutTests
{
    [Fact]
    public async Task Copies_a_normally_progressing_stream_in_full()
    {
        // ~200 KB so the copy spans multiple 81920-byte buffer reads.
        var payload = new byte[200_000];
        for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 251);

        using var source = new MemoryStream(payload);
        using var dest = new MemoryStream();

        await EverefSdeUpdater.CopyWithInactivityTimeoutAsync(
            source, dest, TimeSpan.FromSeconds(5), CancellationToken.None);

        dest.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task Throws_TimeoutException_when_the_source_stalls_past_the_budget()
    {
        using var source = new StallingStream();
        using var dest = new MemoryStream();

        var act = async () => await EverefSdeUpdater.CopyWithInactivityTimeoutAsync(
            source, dest, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Propagates_OperationCanceledException_when_the_caller_cancels()
    {
        using var source = new StallingStream();
        using var dest = new MemoryStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // A genuine caller cancel must surface as OperationCanceledException, NOT be
        // re-labelled a timeout (the caller treats OCE as a real cancel, not Unreachable).
        var act = async () => await EverefSdeUpdater.CopyWithInactivityTimeoutAsync(
            source, dest, TimeSpan.FromSeconds(30), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>A read stream that never produces bytes: each read blocks until its token cancels.</summary>
    private sealed class StallingStream : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0; // unreachable — the delay only completes by throwing on cancellation
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
