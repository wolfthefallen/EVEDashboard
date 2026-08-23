namespace EveDashboard.App.Wpf.Tests.Widgets.StatConfig;

using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;

/// <summary>
/// Unit tests for the Reports "Text size" popup VM. Verifies Load builds
/// one row per element resolving the saved emphasis against each element's DEFAULT
/// (Header0 for a heading, None for the table), Apply stores only the rows that
/// DEVIATE from their default (so an untouched element resolves back to its
/// default), the Apply-failure path keeps the popup open, and Close discards.
/// </summary>
public class TextSizePopupViewModelTests
{
    // Title defaults to a heading look (Header0); Table is plain (None) — Layout 1.
    private static readonly IReadOnlyList<(string Key, string Label, StatEmphasis Default)> Elements = new[]
    {
        ("Title", "Title", StatEmphasis.Header0),
        ("Table", "Table", StatEmphasis.None),
    };

    private static TextSizePopupViewModel NewVm(
        IReadOnlyDictionary<string, StatEmphasis>? saved = null,
        Action<IReadOnlyDictionary<string, StatEmphasis>>? onSave = null) =>
        new(
            Elements,
            _ => Task.FromResult<IReadOnlyDictionary<string, StatEmphasis>?>(saved),
            (map, _) => { onSave?.Invoke(map); return Task.CompletedTask; });

    [Fact]
    public void Ctor_throws_on_null_args()
    {
        Action a1 = () => _ = new TextSizePopupViewModel(
            null!,
            _ => Task.FromResult<IReadOnlyDictionary<string, StatEmphasis>?>(null),
            (_, _) => Task.CompletedTask);
        Action a2 = () => _ = new TextSizePopupViewModel(
            Elements, null!, (_, _) => Task.CompletedTask);
        Action a3 = () => _ = new TextSizePopupViewModel(
            Elements, _ => Task.FromResult<IReadOnlyDictionary<string, StatEmphasis>?>(null), null!);

        a1.Should().Throw<ArgumentNullException>();
        a2.Should().Throw<ArgumentNullException>();
        a3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadAsync_with_no_saved_overrides_resolves_each_row_to_its_default()
    {
        var vm = NewVm(saved: null);

        await vm.LoadAsync();

        vm.Rows.Should().HaveCount(2);
        vm.Rows.Select(r => r.Key).Should().Equal("Title", "Table");
        vm.Rows.First(r => r.Key == "Title").Emphasis.Should().Be(StatEmphasis.Header0);
        vm.Rows.First(r => r.Key == "Table").Emphasis.Should().Be(StatEmphasis.None);
        vm.ShouldClose.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_saved_override_wins_over_default()
    {
        var saved = new Dictionary<string, StatEmphasis> { ["Title"] = StatEmphasis.Hero };
        var vm = NewVm(saved);

        await vm.LoadAsync();

        vm.Rows.First(r => r.Key == "Title").Emphasis.Should().Be(StatEmphasis.Hero);  // override
        vm.Rows.First(r => r.Key == "Table").Emphasis.Should().Be(StatEmphasis.None);  // default
    }

    [Fact]
    public async Task ApplyAsync_stores_only_rows_that_deviate_from_default_and_closes()
    {
        IReadOnlyDictionary<string, StatEmphasis>? saved = null;
        var vm = NewVm(onSave: m => saved = m);
        await vm.LoadAsync();

        // Title (default Header0) -> Header2 deviates and is stored; Table left at its default (None) is dropped.
        vm.Rows.First(r => r.Key == "Title").Emphasis = StatEmphasis.Header2;
        await vm.ApplyAsync();

        saved.Should().NotBeNull();
        saved!.Should().ContainKey("Title").WhoseValue.Should().Be(StatEmphasis.Header2);
        saved.Should().NotContainKey("Table");
        vm.ShouldClose.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_with_everything_at_default_stores_an_empty_map()
    {
        IReadOnlyDictionary<string, StatEmphasis>? saved = null;
        var vm = NewVm(onSave: m => saved = m);
        await vm.LoadAsync();   // Title=Header0 (default), Table=None (default)

        await vm.ApplyAsync();  // no changes -> nothing deviates

        saved.Should().NotBeNull();
        saved!.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_leaves_popup_open_when_save_fails()
    {
        var vm = new TextSizePopupViewModel(
            Elements,
            _ => Task.FromResult<IReadOnlyDictionary<string, StatEmphasis>?>(null),
            (_, _) => throw new InvalidOperationException("boom"));
        await vm.LoadAsync();

        Func<Task> act = async () => await vm.ApplyAsync();

        await act.Should().NotThrowAsync();
        vm.ShouldClose.Should().BeFalse();
    }

    [Fact]
    public void Close_sets_ShouldClose()
    {
        var vm = NewVm();

        vm.Close();

        vm.ShouldClose.Should().BeTrue();
    }

    [Fact]
    public void Generated_commands_exist_with_expected_names()
    {
        // CommunityToolkit.Mvvm's [RelayCommand] strips the Async suffix, so the
        // XAML-bound command properties are LoadCommand / ApplyCommand / CloseCommand.
        var t = typeof(TextSizePopupViewModel);
        t.GetProperty("LoadCommand").Should().NotBeNull("the popup view kicks LoadCommand on open");
        t.GetProperty("ApplyCommand").Should().NotBeNull("the XAML Apply button binds ApplyCommand");
        t.GetProperty("CloseCommand").Should().NotBeNull("the XAML Close button binds CloseCommand");
    }
}
