namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Session;
using FluentAssertions;

/// <summary>
/// Thin VM tests for <see cref="SessionStatsViewModel"/>. Verifies
/// each forwarded stat field returns the controller's value, fires INPC
/// when the matching controller property changes, forwards
/// <c>LastUpdated</c>, no-ops <c>RefreshAsync</c>, and unsubscribes on
/// Dispose.
/// </summary>
public class SessionStatsViewModelTests
{
    [Fact]
    public void Ctor_throws_on_null_controller()
    {
        Action act = () => new SessionStatsViewModel(null!, new FakeSettingsRepository());
        act.Should().Throw<ArgumentNullException>().WithParameterName("controller");
    }

    [Fact]
    public void Ctor_throws_on_null_settings()
    {
        using var f = new SessionControllerTestFixture();
        using var ctrl = f.CreateViewModel();
        Action act = () => new SessionStatsViewModel(ctrl, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Forwarded_stat_fields_return_controller_values()
    {
        using var f = new SessionControllerTestFixture();
        using var ctrl = f.CreateViewModel();
        using var vm = new SessionStatsViewModel(ctrl, new FakeSettingsRepository());

        ctrl.DisplaySessionName = "Hello";
        ctrl.TotalTimeMined = "00:01:23";
        ctrl.TotalM3Mined = 1234.5;
        ctrl.M3PerSecond = 7.5;
        ctrl.TotalIskMined = 1_000_000;
        ctrl.IskPerHour = 500_000;
        ctrl.TotalM3FromCrits = 200;
        ctrl.CritCount = 3;
        ctrl.CritChance = 0.42;

        vm.DisplaySessionName.Should().Be("Hello");
        vm.TotalTimeMined.Should().Be("00:01:23");
        vm.TotalM3Mined.Should().Be(1234.5);
        vm.M3PerSecond.Should().Be(7.5);
        vm.TotalIskMined.Should().Be(1_000_000);
        vm.IskPerHour.Should().Be(500_000);
        vm.TotalM3FromCrits.Should().Be(200);
        vm.CritCount.Should().Be(3);
        vm.CritChance.Should().Be(0.42);
    }

    [Theory]
    [InlineData(nameof(SessionController.DisplaySessionName), nameof(SessionStatsViewModel.DisplaySessionName))]
    [InlineData(nameof(SessionController.TotalTimeMined),     nameof(SessionStatsViewModel.TotalTimeMined))]
    [InlineData(nameof(SessionController.TotalM3Mined),       nameof(SessionStatsViewModel.TotalM3Mined))]
    [InlineData(nameof(SessionController.M3PerSecond),        nameof(SessionStatsViewModel.M3PerSecond))]
    [InlineData(nameof(SessionController.TotalIskMined),      nameof(SessionStatsViewModel.TotalIskMined))]
    [InlineData(nameof(SessionController.IskPerHour),         nameof(SessionStatsViewModel.IskPerHour))]
    [InlineData(nameof(SessionController.TotalM3FromCrits),   nameof(SessionStatsViewModel.TotalM3FromCrits))]
    [InlineData(nameof(SessionController.CritCount),          nameof(SessionStatsViewModel.CritCount))]
    [InlineData(nameof(SessionController.CritChance),         nameof(SessionStatsViewModel.CritChance))]
    public void Controller_property_change_raises_PropertyChanged_for_matching_vm_property(
        string controllerProp, string vmProp)
    {
        using var f = new SessionControllerTestFixture();
        using var ctrl = f.CreateViewModel();
        using var vm = new SessionStatsViewModel(ctrl, new FakeSettingsRepository());
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        // Mutate the named controller property to a non-default value.
        var prop = typeof(SessionController).GetProperty(controllerProp)!;
        var t = prop.PropertyType;
        object value;
        if (t == typeof(string)) value = "x";
        else if (t == typeof(double)) value = 1.0;
        else if (t == typeof(long)) value = 1L;
        else throw new InvalidOperationException($"Unsupported type {t}");
        prop.SetValue(ctrl, value);

        fired.Should().Contain(vmProp);
    }

    [Fact]
    public void Controller_LastUpdated_change_forwards_to_vm_LastUpdated()
    {
        using var f = new SessionControllerTestFixture();
        using var ctrl = f.CreateViewModel();
        using var vm = new SessionStatsViewModel(ctrl, new FakeSettingsRepository());
        var stamp = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

        ctrl.LastUpdated = stamp;

        vm.LastUpdated.Should().Be(stamp);
    }

    [Fact]
    public async Task RefreshAsync_is_a_no_op()
    {
        using var f = new SessionControllerTestFixture();
        using var ctrl = f.CreateViewModel();
        using var vm = new SessionStatsViewModel(ctrl, new FakeSettingsRepository());

        Func<Task> act = async () => await vm.RefreshAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Dispose_unsubscribes_so_post_dispose_controller_changes_dont_fire_vm_events()
    {
        using var f = new SessionControllerTestFixture();
        using var ctrl = f.CreateViewModel();
        var vm = new SessionStatsViewModel(ctrl, new FakeSettingsRepository());
        var fired = new List<string?>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        vm.Dispose();
        ctrl.TotalIskMined = 999;

        fired.Should().NotContain(nameof(SessionStatsViewModel.TotalIskMined));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        using var f = new SessionControllerTestFixture();
        using var ctrl = f.CreateViewModel();
        var vm = new SessionStatsViewModel(ctrl, new FakeSettingsRepository());

        vm.Dispose();
        Action act = () => vm.Dispose();

        act.Should().NotThrow();
    }
}
