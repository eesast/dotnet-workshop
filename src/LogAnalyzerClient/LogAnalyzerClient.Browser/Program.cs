using Avalonia;
using Avalonia.Browser;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using LogAnalyzerClient;
using LogAnalyzerClient.Services;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using static LogAnalyzerRpc.Protos.LogAnalyzerAgentService;

internal sealed partial class Program
{
    internal class ClientFactory : IClientFactory
    {
        public LogAnalyzerAgentServiceClient CreateClient(string address)
        {
            var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler());
            var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions()
                {
                    HttpHandler = handler
                });
            var client = new LogAnalyzerAgentServiceClient(channel);
            return client;
        }
    }

    private static Task Main(string[] args)
    {
        AppService.ClientFactory = new ClientFactory();

        return BuildAvaloniaApp()
            .WithInterFont()
#if DEBUG
            .WithDeveloperTools()
#endif
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}