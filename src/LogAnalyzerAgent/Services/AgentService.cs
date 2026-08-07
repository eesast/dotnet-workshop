using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using LogAnalyzerAgent.Applications;
using LogAnalyzerAgent.Auth;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerAgent.Services
{
    /// <summary>
    /// gRPC 服务实现（T5.1.a.b 起承担鉴权职责）。
    ///
    /// 鉴权方式：客户端在每次调用的 metadata 中携带 <c>authorization: Bearer &lt;token&gt;</c>，
    /// 本服务通过 <see cref="Authorize"/> 从 <see cref="ServerCallContext.RequestHeaders"/> 取出并校验：
    /// <list type="bullet">
    ///   <item>缺失 / 非法 → 抛 <c>RpcException(Unauthenticated)</c>，拒绝该次调用；</item>
    ///   <item>合法 → 得到 <see cref="TokenInfo"/>，作为调用者身份传入业务层，并由
    ///         <see cref="SessionManager"/> 路由到该用户专属的 Analyzer。</item>
    /// </list>
    /// 管理员专用的 token 管理 RPC 还会额外经过 <see cref="RequireAdmin"/> 校验。
    /// </summary>
    public class AgentService : LogAnalyzerAgentService.LogAnalyzerAgentServiceBase
    {
        private readonly AgentSession _session;
        private readonly TokenStore _tokens;

        // 标准的 Bearer token 认证头前缀。
        private const string BearerPrefix = "Bearer ";

        public AgentService(AgentSession session, TokenStore tokens)
        {
            _session = session;
            _tokens = tokens;
        }

        /// <summary>
        /// 从请求 metadata 中解析并校验 token。失败时抛出 <see cref="RpcException"/>，
        /// 由 gRPC 框架转换为对应的状态码返回给客户端。
        /// </summary>
        private TokenInfo Authorize(ServerCallContext context)
        {
            string? headerValue = null;
            foreach (var entry in context.RequestHeaders)
            {
                if (entry.Key == "authorization")
                {
                    headerValue = entry.Value;
                    break;
                }
            }

            string? token = null;
            if (!string.IsNullOrEmpty(headerValue) && headerValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                token = headerValue[BearerPrefix.Length..].Trim();
            }

            TokenInfo? info = token is null ? null : _tokens.TryGet(token);
            if (info is null)
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid token."));
            }
            return info;
        }

        /// <summary>
        /// 要求调用者为管理员，否则抛 <see cref="RpcException"/>。
        /// </summary>
        private static void RequireAdmin(TokenInfo caller)
        {
            if (caller.Role != TokenRole.Admin)
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Admin privilege required for this operation."));
            }
        }

        public override Task<Empty> Ping(Empty empty, ServerCallContext context)
        {
            var caller = Authorize(context);
            return _session.Ping(empty, caller, context.CancellationToken);
        }

        public override Task<GetAgentStatusResponse> GetAgentStatus(Empty empty, ServerCallContext context)
        {
            var caller = Authorize(context);
            return _session.GetAgentStatus(empty, caller, context.CancellationToken);
        }

        public override Task<ChangeDirectoryResponse> ChangeDirectory(ChangeDirectoryRequest request, ServerCallContext context)
        {
            var caller = Authorize(context);
            return _session.ChangeDirectory(request, caller, context.CancellationToken);
        }

        public override Task<GetLogFilesResponse> GetLogFiles(Empty empty, ServerCallContext context)
        {
            var caller = Authorize(context);
            return _session.GetLogFiles(empty, caller, context.CancellationToken);
        }

        public override Task<AnalyzeAllResponse> AnalyzeAll(AnalyzeAllRequest request, ServerCallContext context)
        {
            var caller = Authorize(context);
            return _session.AnalyzeAll(request, caller, context.CancellationToken);
        }

        public override Task<AnalyzeFilesResponse> AnalyzeFiles(AnalyzeFilesRequest request, ServerCallContext context)
        {
            var caller = Authorize(context);
            return _session.AnalyzeFiles(request, caller, context.CancellationToken);
        }

        public override async Task GetAnalysisResult(GetAnalysisResultRequest request, IServerStreamWriter<GetAnalysisResultResponse> responseStream, ServerCallContext context)
        {
            var caller = Authorize(context);
            var responses = _session.GetAnalysisResult(request, caller, context.CancellationToken);
            foreach (var response in responses)
            {
                await responseStream.WriteAsync(response);
            }
        }

        public override async Task QueryAnalysisResult(QueryAnalysisResultRequest request, IServerStreamWriter<GetAnalysisResultResponse> responseStream, ServerCallContext context)
        {
            var caller = Authorize(context);
            var responses = _session.QueryAnalysisResult(request, caller, context.CancellationToken);
            foreach (var response in responses)
            {
                await responseStream.WriteAsync(response);
            }
        }

        public override Task<GetCallTopologyResponse> GetCallTopology(GetCallTopologyRequest request, ServerCallContext context)
        {
            var caller = Authorize(context);
            return Task.FromResult(_session.GetCallTopology(request, caller, context.CancellationToken));
        }

        public override async Task GetEdgeCallLogs(GetEdgeCallLogsRequest request, IServerStreamWriter<GetAnalysisResultResponse> responseStream, ServerCallContext context)
        {
            var caller = Authorize(context);
            var responses = _session.GetEdgeCallLogs(request, caller, context.CancellationToken);
            foreach (var response in responses)
            {
                await responseStream.WriteAsync(response);
            }
        }

        public override async Task GetTrace(GetTraceRequest request, IServerStreamWriter<GetAnalysisResultResponse> responseStream, ServerCallContext context)
        {
            var caller = Authorize(context);
            var responses = _session.GetTrace(request, caller, context.CancellationToken);
            foreach (var response in responses)
            {
                await responseStream.WriteAsync(response);
            }
        }

        public override Task<ExportAnalysisResultResponse> ExportAnalysisResult(ExportAnalysisResultRequest request, ServerCallContext context)
        {
            var caller = Authorize(context);
            return _session.ExportAnalysisResultAsync(request, caller, context.CancellationToken);
        }

        // —— Token 管理 RPC（需管理员权限）——

        public override Task<CreateTokenResponse> CreateToken(CreateTokenRequest request, ServerCallContext context)
        {
            var caller = Authorize(context);
            RequireAdmin(caller);
            return Task.FromResult(_session.CreateToken(request, caller));
        }

        public override Task<OperationStatusMessage> DeleteToken(DeleteTokenRequest request, ServerCallContext context)
        {
            var caller = Authorize(context);
            RequireAdmin(caller);
            return Task.FromResult(_session.DeleteToken(request, caller));
        }

        public override Task<ListTokensResponse> ListTokens(Empty request, ServerCallContext context)
        {
            var caller = Authorize(context);
            RequireAdmin(caller);
            return Task.FromResult(_session.ListTokens(request, caller));
        }

        public override Task<OperationStatusMessage> SetTokenRole(SetTokenRoleRequest request, ServerCallContext context)
        {
            var caller = Authorize(context);
            RequireAdmin(caller);
            return Task.FromResult(_session.SetTokenRole(request, caller));
        }
    }
}
