namespace EveDashboard.App.Wpf.Tests.About;

using EveDashboard.App.Wpf.About;
using EveDashboard.Sde.Meta;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Plan 52: unit tests for <see cref="AboutWindowViewModel"/>. Covers the SDE
/// date formatting (present / unknown / read-failure). <c>VersionText</c> is NOT
/// asserted — it depends on the test-host entry assembly, so it is covered by the
/// manual test instead.
/// </summary>
public class AboutWindowViewModelTests
{
    /// <summary>Hand-rolled fake of <see cref="ISdeMetaReader"/> (no Moq in this project).</summary>
    private sealed class FakeSdeMetaReader : ISdeMetaReader
    {
        private readonly DateTimeOffset? _buildTime;
        private readonly bool _throws;

        public FakeSdeMetaReader(DateTimeOffset? buildTime, bool throws = false)
        {
            _buildTime = buildTime;
            _throws = throws;
        }

        public Task<DateTimeOffset?> GetBuildTimeAsync(CancellationToken ct = default) =>
            _throws
                ? Task.FromException<DateTimeOffset?>(new InvalidOperationException("boom"))
                : Task.FromResult(_buildTime);
    }

    private static AboutWindowViewModel NewVm(ISdeMetaReader reader) =>
        new(reader, NullLogger<AboutWindowViewModel>.Instance);

    [Fact]
    public async Task InitializeAsync_formats_the_build_time_as_yyyy_MM_dd()
    {
        var reader = new FakeSdeMetaReader(new DateTimeOffset(2026, 6, 12, 0, 11, 31, TimeSpan.Zero));
        var vm = NewVm(reader);

        await vm.InitializeAsync();

        vm.SdeDataText.Should().Be("SDE data: 2026-06-12");
    }

    [Fact]
    public async Task InitializeAsync_shows_unknown_when_build_time_is_null()
    {
        var vm = NewVm(new FakeSdeMetaReader(buildTime: null));

        await vm.InitializeAsync();

        vm.SdeDataText.Should().Be("SDE data: unknown");
    }

    [Fact]
    public async Task InitializeAsync_shows_unknown_when_the_reader_throws()
    {
        var vm = NewVm(new FakeSdeMetaReader(buildTime: null, throws: true));

        await vm.InitializeAsync();

        vm.SdeDataText.Should().Be("SDE data: unknown");
    }

    [Fact]
    public void Ctor_throws_on_null_reader()
    {
        Action act = () => _ = new AboutWindowViewModel(null!, NullLogger<AboutWindowViewModel>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }
}
