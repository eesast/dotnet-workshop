using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LogAnalyzerClient;

public partial class ConnectDialog : Window
{
    public ConnectDialog()
    {
        InitializeComponent();
    }

    public ConnectDialog(string currentAddress) : this()
    {
        AddressTextBox.Text = currentAddress;
    }

    private void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(AddressTextBox.Text);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}