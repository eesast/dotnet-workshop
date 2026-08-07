using LogAnalyzerClient.Helpers;
using LogAnalyzerRpc.Protos;
using System;

namespace LogAnalyzerClient.Services
{
    using LogAnalyzerAgentServiceClient = LogAnalyzerAgentService.LogAnalyzerAgentServiceClient;

    public interface IClientFactory
    {
        /// <param name="address">Agent 的地址，如 <c>http://localhost:5000</c>。</param>
        /// <param name="token">用于鉴权的 token（T5.1.a.b）。将被附加到每一次 gRPC 调用的请求头。</param>
        /// <returns>持有 client 及底层 channel 的句柄；重连/断开前需 Dispose 旧句柄以释放 channel。</returns>
        AgentClientHandle CreateClient(string address, string token);
    }

    public class NullClientFactory : IClientFactory
    {
        public AgentClientHandle CreateClient(string address, string token)
        {
            throw new ClientInternalException("Unknown error: No ClientFactory.");
        }
    }

    /// <summary>
    /// 持有 gRPC client 及其底层 channel，便于在重连/断开时 Dispose channel，避免泄漏。
    /// </summary>
    public sealed class AgentClientHandle : IDisposable
    {
        public LogAnalyzerAgentServiceClient Client { get; }
        private readonly IDisposable? _channel;
        private bool _disposed;

        public AgentClientHandle(LogAnalyzerAgentServiceClient client, IDisposable? channel)
        {
            Client = client;
            _channel = channel;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _channel?.Dispose();
        }
    }
}
