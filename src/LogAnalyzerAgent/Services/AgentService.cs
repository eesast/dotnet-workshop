using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzerRpc.Protos;
using LogAnalyzerAgent.Infrastructure;
using LogAnalyzerAgent.Applications;

namespace LogAnalyzerAgent.Services
{
    public class AgentService : LogAnalyzerAgentService.LogAnalyzerAgentServiceBase
    {
        private readonly SessionManager _sessionManager;
        private readonly TokenManager _tokenManager;

        public AgentService(SessionManager sessionManager, TokenManager tokenManager)
        {
            _sessionManager = sessionManager;
            _tokenManager = tokenManager;
        }

        private AgentSession GetSession(ServerCallContext context)
        {
            var token = context.UserState["Token"] as string ?? string.Empty;
            return _sessionManager.GetOrCreateSession(token);
        }

        // ==================== 1. 日志分析路由 (按 Token 隔离) ====================

        public override Task<Empty> Ping(Empty empty, ServerCallContext context)
            => GetSession(context).Ping(empty, context.CancellationToken);

        public override Task<GetAgentStatusResponse> GetAgentStatus(Empty empty, ServerCallContext context)
            => GetSession(context).GetAgentStatus(empty, context.CancellationToken);

        public override Task<ChangeDirectoryResponse> ChangeDirectory(ChangeDirectoryRequest request, ServerCallContext context)
            => GetSession(context).ChangeDirectory(request, context.CancellationToken);

        public override Task<GetLogFilesResponse> GetLogFiles(Empty empty, ServerCallContext context)
            => GetSession(context).GetLogFiles(empty, context.CancellationToken);

        public override Task<AnalyzeAllResponse> AnalyzeAll(AnalyzeAllRequest request, ServerCallContext context)
            => GetSession(context).AnalyzeAllAsync(request, context.CancellationToken);

        public override Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, ServerCallContext context)
            => GetSession(context).AnalyzeFilesAsync(request, context.CancellationToken);

        public override async Task GetAnalysisResult(GetAnalysisResultRequest request, IServerStreamWriter<GetAnalysisResultResponse> responseStream, ServerCallContext context)
        {
            var session = GetSession(context);
            await foreach (var response in session.GetAnalysisResultStreamAsync(request, context.CancellationToken))
            {
                await responseStream.WriteAsync(response, context.CancellationToken);
            }
        }

        // ==================== 3. 查询 / 排序 / 拓扑 ====================

        public override async Task QueryAnalysisResult(QueryAnalysisResultRequest request, IServerStreamWriter<GetAnalysisResultResponse> responseStream, ServerCallContext context)
        {
            var session = GetSession(context);
            await foreach (var response in session.QueryAnalysisResultStreamAsync(request, context.CancellationToken))
            {
                await responseStream.WriteAsync(response, context.CancellationToken);
            }
        }

        public override Task<GetTopologyResponse> GetTopology(GetTopologyRequest request, ServerCallContext context)
            => GetSession(context).GetTopology(request, context.CancellationToken);

        // ==================== 2. 管理员 Token 接口 ====================

        public override Task<CreateTokenResponse> CreateToken(CreateTokenRequest request, ServerCallContext context)
        {
            var created = _tokenManager.CreateToken(request.Role);
            return Task.FromResult(new CreateTokenResponse
            {
                Status = new OperationStatusMessage { Success = true, Code = AgentErrorCode.NoAgentError },
                TokenInfo = new TokenInfo
                {
                    Token = created.Token,
                    Role = created.Role,
                    CreatedAt = Timestamp.FromDateTime(created.CreatedAt.ToUniversalTime())
                }
            });
        }

        public override Task<RevokeTokenResponse> RevokeToken(RevokeTokenRequest request, ServerCallContext context)
        {
            var currentActiveToken = context.UserState["Token"] as string ?? string.Empty;
            bool success = _tokenManager.RevokeToken(request.Token, currentActiveToken);
            if (success)
            {
                _sessionManager.RemoveSession(request.Token);
            }

            return Task.FromResult(new RevokeTokenResponse
            {
                Status = new OperationStatusMessage
                {
                    Success = success,
                    Code = success ? AgentErrorCode.NoAgentError : AgentErrorCode.InvalidArgument,
                    Message = success ? string.Empty : "Cannot revoke active current token or target token not found."
                }
            });
        }

        public override Task<ListTokensResponse> ListTokens(Empty empty, ServerCallContext context)
        {
            var response = new ListTokensResponse
            {
                Status = new OperationStatusMessage { Success = true, Code = AgentErrorCode.NoAgentError }
            };

            foreach (var item in _tokenManager.ListTokens())
            {
                response.Tokens.Add(new TokenInfo
                {
                    Token = item.Token,
                    Role = item.Role,
                    CreatedAt = Timestamp.FromDateTime(item.CreatedAt.ToUniversalTime())
                });
            }

            return Task.FromResult(response);
        }
    }
}