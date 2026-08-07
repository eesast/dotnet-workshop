using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LogAnalyzerClient;

/// <summary>
/// 用户在导出对话框中选择的导出选项。
/// </summary>
internal sealed record ExportOptions(string OutputPath, bool Overwrite);

public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();
    }

    /// <param name="sourceFileName">正在导出的源日志文件名，仅用于提示。</param>
    internal ExportDialog(string sourceFileName) : this()
    {
        SourceTextBlock.Text = $"Source: {sourceFileName}";
    }

    private void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        string path = OutputPathTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(path))
        {
            ErrorTextBlock.Text = "Output path must not be empty.";
            return;
        }
        Close(new ExportOptions(path, OverwriteCheckBox.IsChecked == true));
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
