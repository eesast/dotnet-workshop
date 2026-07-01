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
        private readonly AgentSession _session;

        public AgentService(AgentSession session)
        {
            _session = session;
        }

        public override Task<Empty> Ping(Empty empty, ServerCallContext context)
        {
            return _session.Ping(empty, context.CancellationToken);
        }

        public override Task<GetAgentStatusResponse> GetAgentStatus(Empty empty, ServerCallContext context)
        {
            return _session.GetAgentStatus(empty, context.CancellationToken);
        }

        public override Task<ChangeDirectoryResponse> ChangeDirectory(ChangeDirectoryRequest request, ServerCallContext context)
        {
            throw new NotImplementedException("TODO: T3.1");
        }

        public override Task<GetLogFilesResponse> GetLogFiles(Empty empty, ServerCallContext context)
        {
            throw new NotImplementedException("TODO: T3.1");
        }

        public override Task<AnalyzeAllResponse> AnalyzeAll(AnalyzeAllRequest request, ServerCallContext context)
        {
            throw new NotImplementedException("TODO: T3.1");
        }

        public override Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, ServerCallContext context)
        {
            throw new NotImplementedException("TODO: T3.1");
        }

        public override async Task GetAnalysisResult(GetAnalysisResultRequest request, IServerStreamWriter<GetAnalysisResultResponse> responseStream, ServerCallContext context)
        {
            throw new NotImplementedException("TODO: T3.1");
        }
    }
}
