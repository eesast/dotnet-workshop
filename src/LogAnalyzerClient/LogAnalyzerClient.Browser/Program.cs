using Avalonia;
using Avalonia.Browser;
using Grpc.Core.Interceptors;
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
        public AgentClientHandle CreateClient(string address, string token)
        {
            var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler());
            var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions()
                {
                    HttpHandler = handler
                });
            // 用拦截器把 token 附加到每一次 gRPC 调用，满足 Agent 端的鉴权要求（T5.1.a.b）。
            var client = new LogAnalyzerAgentServiceClient(channel.Intercept(new TokenInterceptor(token)));
            return new AgentClientHandle(client, channel);
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