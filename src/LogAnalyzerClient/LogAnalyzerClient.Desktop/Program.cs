using Avalonia;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using LogAnalyzerClient.Services;
using LogAnalyzerRpc.Protos;
using System;
using static LogAnalyzerRpc.Protos.LogAnalyzerAgentService;

namespace LogAnalyzerClient.Desktop
{
    internal class ClientFactory : IClientFactory
    {
        public LogAnalyzerAgentServiceClient CreateClient(string address, string token)
        {
            var channel = GrpcChannel.ForAddress(address);
            var invoker = channel.CreateCallInvoker().Intercept(metadata =>
            {
                metadata.Add("Authorization", $"Bearer {token}");
                return metadata;
            });
            var client = new LogAnalyzerAgentServiceClient(invoker);
            return client;
        }
    }

    internal sealed class Program
    {

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            AppService.ClientFactory = new ClientFactory();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
