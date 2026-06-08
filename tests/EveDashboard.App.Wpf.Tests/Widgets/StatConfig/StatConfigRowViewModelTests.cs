namespace EveDashboard.App.Wpf.Tests.Widgets.StatConfig;

using System.ComponentModel;
using EveDashboard.App.Wpf.Widgets.StatConfig;
using EveDashboard.Core.Settings.WidgetStats;
using FluentAssertions;

public class StatRowConfigViewModelTests
{
    [Fact]
    public void Ctor_assigns_immutable_fields_and_initial_state()
    {
        var row = new StatConfigRowViewModel("Foo", "Foo Label", reorderable: true, hidden: false, StatEmphasis.Header0);
        row.Key.Should().Be("Foo");
        row.Label.Should().Be("Foo Label");
        row.Reorderable.Should().BeTrue();
        row.Hidden.Should().BeFalse();
        row.Emphasis.Should().Be(StatEmphasis.Header0);
    }

    [Fact]
    public void Hidden_setter_raises_PropertyChanged()
    {
        var row = new StatConfigRowViewModel("Foo", "F", true, false, StatEmphasis.None);
        var fired = new List<string?>();
        row.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        row.Hidden = true;

        fired.Should().Contain(nameof(StatConfigRowViewModel.Hidden));
    }

    [Fact]
    public void Emphasis_setter_raises_PropertyChanged()
    {
        var row = new StatConfigRowViewModel("Foo", "F", true, false, StatEmphasis.None);
        var fired = new List<string?>();
        row.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        row.Emphasis = StatEmphasis.Hero;

        fired.Should().Contain(nameof(StatConfigRowViewModel.Emphasis));
    }

    [Fact]
    public void Ctor_throws_on_null_key()
    {
        Action act = () => _ = new StatConfigRowViewModel(null!, "L", true, false, StatEmphasis.None);
        act.Should().Throw<ArgumentNullException>().WithParameterName("key");
    }

    [Fact]
    public void Ctor_throws_on_null_label()
    {
        Action act = () => _ = new StatConfigRowViewModel("K", null!, true, false, StatEmphasis.None);
        act.Should().Throw<ArgumentNullException>().WithParameterName("label");
    }
}
