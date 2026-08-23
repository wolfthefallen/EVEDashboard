namespace EveDashboard.App.Wpf.Tests.Geometry;

using EveDashboard.App.Wpf.Geometry;
using EveDashboard.Core.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Apply-side (restore-onto-Window) behaviour is deliberately not unit-tested
/// here: it would require constructing an STA Window, so it is covered by
/// manual verification instead. Persistence-side is covered by the
/// SessionController test fixture's stats round-trip.
///
/// WindowGeometryService takes read/write delegates instead of an
/// ISettingsRepository, so tests construct via the delegate ctor.
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
