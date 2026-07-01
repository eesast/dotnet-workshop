using LogAnalyzerClient.Helpers;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerClient.Services
{
    using LogAnalyzerAgentServiceClient = LogAnalyzerAgentService.LogAnalyzerAgentServiceClient;

    public interface IClientFactory
    {
        LogAnalyzerAgentServiceClient CreateClient(string address);
    }

    public class NullClientFactory : IClientFactory
    {
        public LogAnalyzerAgentServiceClient CreateClient(string address)
        {
            throw new ClientInternalException("Unknown error: No ClientFactory.");
        }
    }
}
