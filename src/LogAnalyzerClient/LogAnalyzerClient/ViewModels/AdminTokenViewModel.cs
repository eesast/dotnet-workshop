using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using LogAnalyzerClient.Services;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerClient.ViewModels;

public class AdminTokenViewModel
{
    private readonly LogAnalyzerAgentService.LogAnalyzerAgentServiceClient _client;

    public ObservableCollection<TokenInfo> TokenList { get; set; } = new();

    public AdminTokenViewModel(string serverUrl = "http://localhost:5000")
    {
        _client = LogAgentClientManager.CreateClient(serverUrl);
    }

    public async Task RefreshTokensAsync()
    {
        var response = await _client.ListTokensAsync(new Empty());
        if (response.Status.Success)
        {
            TokenList.Clear();
            foreach (var token in response.Tokens)
            {
                TokenList.Add(token);
            }
        }
    }

    public async Task<bool> CreateTokenAsync(TokenRole role)
    {
        var response = await _client.CreateTokenAsync(new CreateTokenRequest { Role = role });
        if (response.Status.Success)
        {
            await RefreshTokensAsync();
            return true;
        }
        return false;
    }

    public async Task<bool> RevokeTokenAsync(string token)
    {
        var response = await _client.RevokeTokenAsync(new RevokeTokenRequest { Token = token });
        if (response.Status.Success)
        {
            await RefreshTokensAsync();
            return true;
        }
        return false;
    }
}