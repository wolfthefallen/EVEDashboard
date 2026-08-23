namespace EveDashboard.App.Wpf.Tests;

using EveDashboard.App.Wpf;
using EveDashboard.App.Wpf.Tests.Helpers;
using FluentAssertions;

/// <summary>
/// Trivial composition-class tests for
/// <see cref="MainWindowViewModel"/>. Ctor null-arg + property routing through
/// the <see cref="EveDashboard.App.Wpf.Layout.LayoutCoordinator"/>.
/// </summary>
public class MainWindowViewModelTests
{
    [Fact]
    public void Ctor_throws_on_null_layout()
    {
        Action act = () => new MainWindowViewModel(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("layout");
    }

    [Fact]
    public void Layout_property_exposes_injected_coordinator()
    {
        using var f = new LayoutCoordinatorTestFixture();
        using var coord = f.CreateCoordinator();
        var vm = new MainWindowViewModel(coord);
        vm.Layout.Should().BeSameAs(coord);
    }

    [Fact]
    public void Session_property_routes_to_coordinator_session_view_model()
    {
        using var f = new LayoutCoordinatorTestFixture();
        using var coord = f.CreateCoordinator();
        var vm = new MainWindowViewModel(coord);
        vm.Session.Should().BeSameAs(coord.SessionViewModel);
    }
}
