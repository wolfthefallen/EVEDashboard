namespace EveDashboard.App.Wpf.Tests.Widgets.StatConfig;

using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;

public class StatConfigPopupViewModelTests
{
    private sealed class SampleVm
    {
        [StatRow("Alpha", DefaultOrder = 1)] public int Alpha { get; set; }
        [StatRow("Bravo", DefaultOrder = 2, DefaultEmphasis = StatEmphasis.Header0)] public int Bravo { get; set; }
        [StatRow("Footer", Reorderable = false, DefaultOrder = 9999)] public int Footer { get; set; }
    }

    private static StatConfigPopupViewModel NewVm(
        WidgetStatsConfig? saved = null,
        Action<WidgetStatsConfig>? onSave = null,
        bool throwOnSave = false)
    {
        return new StatConfigPopupViewModel(
            vmType: typeof(SampleVm),
            load: _ => Task.FromResult(saved),
            save: (cfg, _) =>
            {
                if (throwOnSave) throw new InvalidOperationException("test failure");
                onSave?.Invoke(cfg);
                return Task.CompletedTask;
            });
    }

    [Fact]
    public void Ctor_throws_on_null_args()
    {
        Action a1 = () => _ = new StatConfigPopupViewModel(null!, _ => Task.FromResult<WidgetStatsConfig?>(null), (_, _) => Task.CompletedTask);
        Action a2 = () => _ = new StatConfigPopupViewModel(typeof(SampleVm), null!, (_, _) => Task.CompletedTask);
        Action a3 = () => _ = new StatConfigPopupViewModel(typeof(SampleVm), _ => Task.FromResult<WidgetStatsConfig?>(null), null!);

        a1.Should().Throw<ArgumentNullException>();
        a2.Should().Throw<ArgumentNullException>();
        a3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadAsync_with_null_saved_populates_default_lists()
    {
        using var vm = NewVm(saved: null);

        await vm.LoadAsync();

        vm.Reorderable.Select(r => r.Key).Should().Equal("Alpha", "Bravo");
        vm.Reorderable.First(r => r.Key == "Bravo").Emphasis.Should().Be(StatEmphasis.Header0);
        vm.Reorderable.Should().AllSatisfy(r => r.Hidden.Should().BeFalse());

        vm.HideOnly.Should().ContainSingle().Which.Key.Should().Be("Footer");
    }

    [Fact]
    public async Task LoadAsync_with_saved_uses_saved_order_and_emphasis_and_hidden()
    {
        var saved = new WidgetStatsConfig(new[]
        {
            new StatEntry("Bravo",  true,  StatEmphasis.Hero),
            new StatEntry("Alpha",  false, StatEmphasis.None),
        });
        using var vm = NewVm(saved);

        await vm.LoadAsync();

        vm.Reorderable.Select(r => r.Key).Should().Equal("Bravo", "Alpha");
        vm.Reorderable.First(r => r.Key == "Bravo").Hidden.Should().BeTrue();
        vm.Reorderable.First(r => r.Key == "Bravo").Emphasis.Should().Be(StatEmphasis.Hero);
    }

    [Fact]
    public async Task ApplyAsync_projects_working_copy_to_saved_config()
    {
        WidgetStatsConfig? captured = null;
        using var vm = NewVm(saved: null, onSave: cfg => captured = cfg);
        await vm.LoadAsync();

        // Edit the working copy: hide Alpha, set Bravo emphasis to Hero.
        vm.Reorderable.First(r => r.Key == "Alpha").Hidden = true;
        vm.Reorderable.First(r => r.Key == "Bravo").Emphasis = StatEmphasis.Hero;

        await vm.ApplyAsync();

        captured.Should().NotBeNull();
        var entries = captured!.Entries;
        entries.First(e => e.StatKey == "Alpha").Hidden.Should().BeTrue();
        entries.First(e => e.StatKey == "Bravo").Emphasis.Should().Be(StatEmphasis.Hero);
    }

    [Fact]
    public async Task ApplyAsync_success_sets_ShouldClose_true()
    {
        using var vm = NewVm();
        await vm.LoadAsync();

        await vm.ApplyAsync();

        vm.ShouldClose.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_failure_leaves_ShouldClose_false()
    {
        using var vm = NewVm(throwOnSave: true);
        await vm.LoadAsync();

        await vm.ApplyAsync(); // does not throw — VM swallows + logs

        vm.ShouldClose.Should().BeFalse();
    }

    [Fact]
    public async Task Close_sets_ShouldClose_true_without_calling_save()
    {
        var saveCalled = false;
        using var vm = NewVm(saved: null, onSave: _ => saveCalled = true);
        await vm.LoadAsync();

        vm.Close();

        vm.ShouldClose.Should().BeTrue();
        saveCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_LoadAsync_resets_working_copy_to_saved_state()
    {
        using var vm = NewVm(saved: null);
        await vm.LoadAsync();
        vm.Reorderable.First(r => r.Key == "Alpha").Hidden = true;

        // Re-load: working copy should reset (saved is still null = defaults).
        await vm.LoadAsync();

        vm.Reorderable.First(r => r.Key == "Alpha").Hidden.Should().BeFalse();
    }

    [Fact]
    public async Task Drag_reorder_in_Reorderable_persists_on_Apply()
    {
        WidgetStatsConfig? captured = null;
        using var vm = NewVm(saved: null, onSave: cfg => captured = cfg);
        await vm.LoadAsync();

        // Simulate a drag: move Bravo before Alpha.
        var bravo = vm.Reorderable.First(r => r.Key == "Bravo");
        vm.Reorderable.Remove(bravo);
        vm.Reorderable.Insert(0, bravo);

        await vm.ApplyAsync();

        captured!.Entries.Where(e => e.StatKey is "Alpha" or "Bravo")
            .Select(e => e.StatKey).Should().Equal("Bravo", "Alpha");
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var vm = NewVm();

        vm.Dispose();
        Action act = () => vm.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Generated_command_properties_match_the_names_XAML_binds_to()
    {
        // Regression test for a dead-button bug (2026-05-05): the popup XAML binds
        // to ApplyCommand / LoadCommand / CloseCommand. CommunityToolkit.Mvvm's
        // [RelayCommand] source generator strips the `Async` suffix from method
        // names, so [RelayCommand] async Task ApplyAsync() emits a property
        // named ApplyCommand (not ApplyAsyncCommand). If anyone renames a VM
        // method or rebinds the XAML to the wrong name, this test catches it
        // before the user discovers it via a dead button.
        var t = typeof(StatConfigPopupViewModel);
        t.GetProperty("ApplyCommand").Should().NotBeNull("XAML Apply button binds to ApplyCommand");
        t.GetProperty("CloseCommand").Should().NotBeNull("XAML Close button binds to CloseCommand");
        t.GetProperty("LoadCommand").Should().NotBeNull("future XAML LoadCommand binding (and the generator output sanity check)");

        // Ensure the misspelled-suffix variant does NOT exist — guards against
        // the exact regression that shipped the dead-Apply-button bug.
        t.GetProperty("ApplyAsyncCommand").Should().BeNull("the source generator strips the Async suffix");
    }
}
