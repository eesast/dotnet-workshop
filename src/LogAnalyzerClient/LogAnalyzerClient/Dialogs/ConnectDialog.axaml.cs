using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LogAnalyzerClient;

/// <summary>
/// 连接对话框的返回值：Agent 地址 + 鉴权 token（T5.1.a.b）。
/// </summary>
internal sealed record ConnectResult(string Address, string Token);

public partial class ConnectDialog : Window
{
    public ConnectDialog()
    {
        InitializeComponent();
    }

    private void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        // 返回地址与 token；校验是否为空交给调用方（便于给出统一的错误提示）。
        Close(new ConnectResult(AddressTextBox.Text ?? "", TokenTextBox.Text ?? ""));
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
