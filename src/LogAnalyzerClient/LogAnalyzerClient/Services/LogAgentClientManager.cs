using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerClient.Services;

public static class LogAgentClientManager
{
    public static string CurrentToken { get; set; } = string.Empty;

    private const string DefaultServerUrl = "http://localhost:5000";

    public static LogAnalyzerAgentService.LogAnalyzerAgentServiceClient CreateClient()
    {
        return CreateClient(DefaultServerUrl);
    }

    public static LogAnalyzerAgentService.LogAnalyzerAgentServiceClient CreateClient(string serverUrl)
    {
        var callCredentials = CallCredentials.FromInterceptor((context, metadata) =>
        {
            if (!string.IsNullOrEmpty(CurrentToken))
            {
                metadata.Add("x-agent-token", CurrentToken);
            }
            return Task.CompletedTask;
        });

        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Create(
                ChannelCredentials.Insecure,
                callCredentials
            ),
            UnsafeUseInsecureChannelCallCredentials = true
        };

        var channel = GrpcChannel.ForAddress(serverUrl, channelOptions);
        return new LogAnalyzerAgentService.LogAnalyzerAgentServiceClient(channel);
    }
}