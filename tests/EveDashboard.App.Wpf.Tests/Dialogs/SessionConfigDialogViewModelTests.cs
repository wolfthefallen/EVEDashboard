namespace EveDashboard.App.Wpf.Tests.Dialogs;

using EveDashboard.App.Wpf.Dialogs;
using EveDashboard.App.Wpf.Tests.Helpers;
using EveDashboard.App.Wpf.Widgets.Session;
using FluentAssertions;

/// <summary>
/// Bug-1 regression tests for <see cref="SessionConfigDialogViewModel"/>.
/// The dialog must flip <see cref="SessionConfigDialogViewModel.ShouldClose"/>
/// to <c>true</c> when StartCommand succeeds — under plan 37 that means the
/// controller transitions into <see cref="SessionWidgetMode.Paused"/>
/// (Start-as-paused) rather than the pre-plan-37 Running mode.
/// </summary>
public class SessionConfigDialogViewModelTests
{
    [Fact]
    public async Task ShouldClose_flips_true_when_controller_transitions_to_Paused_via_StartCommand()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        using var vm = new SessionConfigDialogViewModel(controller);
        vm.ResetForNewOpen();
        vm.ShouldClose.Should().BeFalse("preconditions: dialog opens with ShouldClose=false");

        // Configure + Start via the dialog VM. The controller's StartCommand
        // runs synchronously to the await; under plan 37 it lands in Paused.
        vm.SessionName = "Bug-1 close";
        controller.Pilots[0].IsSelected = true;
        await controller.StartCommand.ExecuteAsync(null);

        try
        {
            controller.Mode.Should().Be(SessionWidgetMode.Paused, "plan 37 lands in Paused");
            vm.ShouldClose.Should().BeTrue("the dialog must close on Start success even though Mode is Paused, not Running");
        }
        finally
        {
            await controller.StopCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public void ResetForNewOpen_clears_ShouldClose_so_a_stale_true_does_not_prevent_user_seeing_the_dialog()
    {
        using var f = new SessionControllerTestFixture();
        using var controller = f.CreateViewModel();
        using var vm = new SessionConfigDialogViewModel(controller);

        // Simulate a stale ShouldClose=true from a prior dialog open.
        vm.CancelCommand.Execute(null);
        vm.ShouldClose.Should().BeTrue();

        vm.ResetForNewOpen();

        vm.ShouldClose.Should().BeFalse();
    }
}
