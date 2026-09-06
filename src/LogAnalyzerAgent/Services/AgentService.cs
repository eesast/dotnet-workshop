using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogAnalyzerRpc;
using LogParser.Visitors;
using LogAnalyzerAgent.Applications;

namespace LogAnalyzerAgent.Services
{
    public class AgentService : LogAnalyzerAgentService.LogAnalyzerAgentServiceBase
    {
        private readonly AgentSessionRegistry _sessionRegistry;

        public AgentService(AgentSessionRegistry sessionRegistry)
        {
            _sessionRegistry = sessionRegistry;
        }

        public override Task<Empty> Ping(Empty empty, ServerCallContext context)
        {
            var session = _sessionRegistry.GetOrCreateSession(context);
            return session.Ping(empty, context.CancellationToken);
        }

        public override Task<GetAgentStatusResponse> GetAgentStatus(Empty empty, ServerCallContext context)
        {
            var session = _sessionRegistry.GetOrCreateSession(context);
            return session.GetAgentStatus(empty, context.CancellationToken);
        }

        public override Task<ChangeDirectoryResponse> ChangeDirectory(ChangeDirectoryRequest request, ServerCallContext context)
        {
            var session = _sessionRegistry.GetOrCreateSession(context);
            return session.ChangeDirectory(request, context.CancellationToken);
        }

        public override Task<GetLogFilesResponse> GetLogFiles(Empty empty, ServerCallContext context)
        {
            var session = _sessionRegistry.GetOrCreateSession(context);
            return session.GetLogFiles(empty, context.CancellationToken);
        }

        public override Task<AnalyzeAllResponse> AnalyzeAll(AnalyzeAllRequest request, ServerCallContext context)
        {
            var session = _sessionRegistry.GetOrCreateSession(context);
            return session.AnalyzeAll(request, context.CancellationToken);
        }

        public override Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, ServerCallContext context)
        {
            var session = _sessionRegistry.GetOrCreateSession(context);
            return session.AnalyzeFiles(request, context.CancellationToken);
        }

        public override async Task GetAnalysisResult(GetAnalysisResultRequest request, IServerStreamWriter<GetAnalysisResultResponse> responseStream, ServerCallContext context)
        {
            var session = _sessionRegistry.GetOrCreateSession(context);
            var responses = session.GetAnalysisResult(request, context.CancellationToken);
            foreach (var response in responses)
            {
                await responseStream.WriteAsync(response);
            }
        }

        public override Task<SaveAnalysisResultResponse> SaveAnalysisResult(SaveAnalysisResultRequest request, ServerCallContext context)
        {
            var session = _sessionRegistry.GetOrCreateSession(context);
            return session.SaveAnalysisResult(request);
        }
    }
}
