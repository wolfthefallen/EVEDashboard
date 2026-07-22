namespace EveDashboard.App.Wpf.Diagnostics;

/// <summary>
/// Test seam over the "unexpected error" dialog so <see cref="GlobalExceptionHandler"/>
/// can be unit-tested without popping a real window. Production wires
/// <see cref="MessageBoxErrorDialog"/>.
/// </summary>
public interface IUnhandledErrorDialog
{
    /// <summary>Show a modal "something went wrong" message to the user.</summary>
    /// <param name="message">The user-facing message (already formatted, Ctrl+C-copyable).</param>
    void Show(string message);
}
