using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Google.Protobuf.WellKnownTypes;
using LogAnalyzerClient.Models;
using LogAnalyzerRpc.Protos;
using LogAnalyzerAgentServiceClient = LogAnalyzerRpc.Protos.LogAnalyzerAgentService.LogAnalyzerAgentServiceClient;

namespace LogAnalyzerClient;

/// <summary>
/// 管理员窗口（T5.1.a.b）：列出、创建、删除 token，以及提升 / 降低权限。
/// 直接持有 gRPC 客户端；每次操作后整体刷新列表。非管理员调用会被 Agent 以
/// PermissionDenied 拒绝，本窗口据此给出提示。
/// </summary>
public partial class TokenManagerWindow : Window
{
    // 仅由带参构造函数赋值；Avalonia 的 XAML 加载器走无参构造函数时不会访问这两个字段，故以 null! 抑制告警。
    private readonly LogAnalyzerAgentServiceClient _client = null!;
    private readonly string _callerToken = "";

    // 列表数据源；在代码里赋给 TokenListControl.ItemsSource，避免根级编译绑定。
    private readonly ObservableCollection<TokenRowVm> _rows = new();

    public TokenManagerWindow()
    {
        InitializeComponent();
    }

    internal TokenManagerWindow(LogAnalyzerAgentServiceClient client, string callerToken) : this()
    {
        _client = client;
        _callerToken = callerToken ?? "";
        TokenListControl.ItemsSource = _rows;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        StatusTextBlock.Text = "Loading...";
        try
        {
            var resp = await _client.ListTokensAsync(new Empty());
            _rows.Clear();
            foreach (var t in resp.Tokens)
            {
                _rows.Add(new TokenRowVm(t.Token, t.Role, t.Note, t.Token == _callerToken));
            }
            StatusTextBlock.Text = $"{_rows.Count} token(s).";
        }
        catch (Grpc.Core.RpcException ex)
        {
            StatusTextBlock.Text = ex.StatusCode == Grpc.Core.StatusCode.PermissionDenied
                ? "Admin privilege required to manage tokens."
                : $"Error: {ex.Status.Detail}";
        }
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
        => await RefreshAsync();

    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        var role = RoleComboBox.SelectedIndex == 1 ? TokenRoleEnum.TokenAdmin : TokenRoleEnum.TokenNormal;
        string note = NoteTextBox.Text?.Trim() ?? "";
        try
        {
            var resp = await _client.CreateTokenAsync(new CreateTokenRequest { Role = role, Note = note });
            // 新 token 只显示一次（管理员可在此选中复制后交给用户）。
            StatusTextBlock.Text = $"Created [{resp.Token.Role}] token:\n{resp.Token.Token}";
            NoteTextBox.Text = "";
            await RefreshAsync();
        }
        catch (Grpc.Core.RpcException ex)
        {
            StatusTextBlock.Text = $"Create failed: {ex.Status.Detail}";
        }
    }

    private async void ToggleRoleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not TokenRowVm row)
        {
            return;
        }
        var newRole = row.Role == TokenRoleEnum.TokenAdmin ? TokenRoleEnum.TokenNormal : TokenRoleEnum.TokenAdmin;
        try
        {
            var st = await _client.SetTokenRoleAsync(new SetTokenRoleRequest { Token = row.Token, Role = newRole });
            StatusTextBlock.Text = st.Success
                ? $"Token set to {newRole}."
                : $"Failed: {st.Message}";
            await RefreshAsync();
        }
        catch (Grpc.Core.RpcException ex)
        {
            StatusTextBlock.Text = $"Toggle role failed: {ex.Status.Detail}";
        }
    }

    private async void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string token)
        {
            return;
        }
        try
        {
            var st = await _client.DeleteTokenAsync(new DeleteTokenRequest { Token = token });
            StatusTextBlock.Text = st.Success ? "Token deleted." : $"Failed: {st.Message}";
            await RefreshAsync();
        }
        catch (Grpc.Core.RpcException ex)
        {
            StatusTextBlock.Text = $"Delete failed: {ex.Status.Detail}";
        }
    }

}
