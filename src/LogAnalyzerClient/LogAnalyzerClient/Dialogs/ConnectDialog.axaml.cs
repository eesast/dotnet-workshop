using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LogAnalyzerClient;

public record ConnectInfo(string? Address, string? Token);
public partial class ConnectDialog : Window
{
    public ConnectDialog()
    {
        InitializeComponent();
    }

    public ConnectDialog(string currentAddress, string token) : this()
    {
        AddressTextBox.Text = currentAddress;
        TokenTextBox.Text = token;
    }

    private void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(new ConnectInfo(AddressTextBox.Text, TokenTextBox.Text));
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}