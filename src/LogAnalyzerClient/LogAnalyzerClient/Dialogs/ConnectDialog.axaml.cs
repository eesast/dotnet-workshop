using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LogAnalyzerClient;

/// <summary>连接对话框返回的信息：Agent 地址 + Token。</summary>
public sealed record ConnectInfo(string Address, string Token);

public partial class ConnectDialog : Window
{
    public ConnectDialog()
    {
        InitializeComponent();
    }

    public ConnectDialog(string currentAddress, string currentToken) : this()
    {
        AddressTextBox.Text = currentAddress;
        TokenTextBox.Text = currentToken;
    }

    private void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(new ConnectInfo(AddressTextBox.Text ?? string.Empty, TokenTextBox.Text ?? string.Empty));
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
