namespace EveDashboard.App.Wpf.Tests.Geometry;

using EveDashboard.App.Wpf.Geometry;
using EveDashboard.Core.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Per plan 16 alignment 2.1 = C: drop apply-side tests (would require an STA
/// Window construction); cover apply-side end-to-end via the manual test doc
/// (<c>tests/manual/manual-tests.md</c> Section 1, step 9). Persistence-side
/// is covered by the SessionController test fixture's stats round-trip.
///
/// Plan 25 refactor: WindowGeometryService now takes read/write delegates
/// instead of an ISettingsRepository. Tests construct via the delegate ctor.
/// </summary>
public class WindowGeometryServiceTests
{
    private static Func<CancellationToken, Task<WindowGeometry?>> NullRead =>
        _ => Task.FromResult<WindowGeometry?>(null);

    private static Func<WindowGeometry, Task> NoopWrite =>
        _ => Task.CompletedTask;

    [Fact]
    public void Ctor_throws_on_null_read_delegate()
    {
        Action act = () => new WindowGeometryService(
            null!, NoopWrite, 1280, 800, NullLogger<WindowGeometryService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("readGeometry");
    }

    [Fact]
    public void Ctor_throws_on_null_write_delegate()
    {
        Action act = () => new WindowGeometryService(
            NullRead, null!, 1280, 800, NullLogger<WindowGeometryService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("writeGeometry");
    }

    [Fact]
    public void Ctor_throws_on_null_log()
    {
        Action act = () => new WindowGeometryService(
            NullRead, NoopWrite, 1280, 800, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("log");
    }

    [Fact]
    public async Task ApplyOnStartupAsync_throws_on_null_window()
    {
        var sut = new WindowGeometryService(
            NullRead, NoopWrite, 1280, 800, NullLogger<WindowGeometryService>.Instance);
        Func<Task> act = async () => await sut.ApplyOnStartupAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("window");
    }
}
