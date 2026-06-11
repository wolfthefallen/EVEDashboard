namespace EveDashboard.App.Wpf.Tests.Helpers;

using EveDashboard.App.Wpf.Dialogs;

/// <summary>Test seam — returns a configurable bool; counts <see cref="Show"/> invocations and remembers the last strings supplied.</summary>
internal sealed class FakeConfirmDialog : IConfirmDialog
{
    private readonly bool _result;
    public int ShowCount { get; private set; }
    public string? LastTitle { get; private set; }
    public string? LastBody { get; private set; }
    public string? LastConfirmLabel { get; private set; }
    public string? LastCancelLabel { get; private set; }

    public FakeConfirmDialog(bool result) { _result = result; }

    public bool Show(string title, string body, string confirmLabel, string cancelLabel = "Cancel")
    {
        ShowCount++;
        LastTitle = title;
        LastBody = body;
        LastConfirmLabel = confirmLabel;
        LastCancelLabel = cancelLabel;
        return _result;
    }
}
