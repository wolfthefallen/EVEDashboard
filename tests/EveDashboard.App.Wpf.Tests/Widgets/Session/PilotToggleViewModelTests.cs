namespace EveDashboard.App.Wpf.Tests.Widgets.Session;

using System.ComponentModel;
using System.IO;
using EveDashboard.App.Wpf.Widgets.Session;
using EveDashboard.Core.Domain;
using FluentAssertions;

public class PilotToggleViewModelTests
{
    private static Character SamplePilot() =>
        new(2_000_000_001, "Alice", Path.Combine(Path.GetTempPath(), "alice.txt"));

    [Fact]
    public void Ctor_throws_on_null_character()
    {
        Action act = () => new PilotToggleViewModel(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("character");
    }

    [Fact]
    public void Construction_sets_Character_and_IsSelected_false()
    {
        var character = SamplePilot();
        var sut = new PilotToggleViewModel(character);

        sut.Character.Should().BeSameAs(character);
        sut.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void IsSelected_round_trips_via_INPC()
    {
        var sut = new PilotToggleViewModel(SamplePilot());
        var raisedFor = new List<string?>();
        ((INotifyPropertyChanged)sut).PropertyChanged += (_, e) => raisedFor.Add(e.PropertyName);

        sut.IsSelected = true;
        sut.IsSelected = false;

        sut.IsSelected.Should().BeFalse();
        raisedFor.Should().Equal(nameof(PilotToggleViewModel.IsSelected), nameof(PilotToggleViewModel.IsSelected));
    }
}
