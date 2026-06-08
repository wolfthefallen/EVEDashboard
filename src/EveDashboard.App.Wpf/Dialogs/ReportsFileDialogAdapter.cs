namespace EveDashboard.App.Wpf.Dialogs;

using Microsoft.Win32;

/// <summary>
/// Production <see cref="IReportsFileDialog"/> implementation wrapping the
/// stock <see cref="OpenFileDialog"/> / <see cref="SaveFileDialog"/> from
/// <see cref="Microsoft.Win32"/>. Defaults to the user's My Documents folder.
/// </summary>
public sealed class ReportsFileDialogAdapter : IReportsFileDialog
{
    private static string DefaultDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <inheritdoc />
    public string? PickOpenJsonPath()
    {
        var dialog = new OpenFileDialog
        {
            Filter           = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt       = ".json",
            InitialDirectory = DefaultDirectory,
            Title            = "Import session from JSON",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickSaveJsonPath(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter           = "JSON files (*.json)|*.json",
            DefaultExt       = ".json",
            FileName         = defaultName,
            InitialDirectory = DefaultDirectory,
            Title            = "Export session to JSON",
            OverwritePrompt  = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickSaveExcelPath(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            Filter           = "Excel workbooks (*.xlsx)|*.xlsx",
            DefaultExt       = ".xlsx",
            FileName         = defaultName,
            InitialDirectory = DefaultDirectory,
            Title            = "Export session to Excel",
            OverwritePrompt  = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
