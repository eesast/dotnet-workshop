using Avalonia.Controls;
using Avalonia.Interactivity;
using LogAnalyzerClient.Services;
using LogAnalyzerClient.ViewModels;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerClient;

public partial class AdminTokenWindow : Window
{
    private readonly AdminTokenViewModel _viewModel;

    public AdminTokenWindow() : this("http://localhost:5000")
    {
    }

    public AdminTokenWindow(string serverUrl)
    {
        InitializeComponent();
        _viewModel = new AdminTokenViewModel(serverUrl);
        DataContext = _viewModel;
    }

    // 应用 Token 并向后端发送请求
    private async void OnApplyTokenClick(object? sender, RoutedEventArgs e)
    {
        var tokenInput = TokenTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(tokenInput))
        {
            return;
        }

        // 动态设置当前全局通信用的 Token
        LogAgentClientManager.CurrentToken = tokenInput;

        // 重新请求后端列表
        await _viewModel.RefreshTokensAsync();
    }

    private async void OnCreateTokenClick(object? sender, RoutedEventArgs e)
    {
        var role = RoleComboBox.SelectedIndex == 1 ? TokenRole.RoleAdmin : TokenRole.RoleNormal;
        await _viewModel.CreateTokenAsync(role);
    }

    private async void OnRevokeTokenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is TokenInfo tokenInfo)
        {
            await _viewModel.RevokeTokenAsync(tokenInfo.Token);
        }
    }
}