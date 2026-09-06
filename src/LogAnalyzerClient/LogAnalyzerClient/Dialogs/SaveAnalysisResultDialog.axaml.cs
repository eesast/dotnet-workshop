using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;

namespace LogAnalyzerClient;

public partial class SaveAnalysisResultDialog : Window
{
    private bool _submitted;

    public SaveAnalysisResultDialog()
    {
        InitializeComponent();
        Closing += (_, e) => e.Cancel = !_submitted;
    }

    public SaveAnalysisResultDialog(string fileName) : this()
    {
        FileNameRun.Text = fileName;
    }

    private void DirectoryPathTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(DirectoryPathTextBox.Text);
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        string directoryPath = DirectoryPathTextBox.Text!.Trim();
        _submitted = true;
        Close(directoryPath);
    }
}
