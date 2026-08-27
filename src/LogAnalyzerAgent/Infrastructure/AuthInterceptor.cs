using Grpc.Core;
using Grpc.Core.Interceptors;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerAgent.Infrastructure
{
    public class AuthInterceptor : Interceptor
    {
        private readonly TokenManager _tokenManager;

        // Proto 对应的完整路径：/log_analyzer.v1.LogAnalyzerAgentService/方法名
        private static readonly HashSet<string> AdminOnlyMethods = new()
        {
            "/log_analyzer.v1.LogAnalyzerAgentService/CreateToken",
            "/log_analyzer.v1.LogAnalyzerAgentService/RevokeToken",
            "/log_analyzer.v1.LogAnalyzerAgentService/ListTokens"
        };

        public AuthInterceptor(TokenManager tokenManager)
        {
            _tokenManager = tokenManager;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            ValidateAndContextualize(context);
            return await continuation(request, context);
        }

        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            ValidateAndContextualize(context);
            await continuation(request, responseStream, context);
        }

        private void ValidateAndContextualize(ServerCallContext context)
        {
            var token = context.RequestHeaders.GetValue("x-agent-token");
            if (string.IsNullOrEmpty(token))
            {
                var authHeader = context.RequestHeaders.GetValue("authorization");
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = authHeader.Substring("Bearer ".Length).Trim();
                }
            }

            if (string.IsNullOrEmpty(token) || !_tokenManager.ValidateToken(token, out var role))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid or missing Agent Authentication Token."));
            }

            if (AdminOnlyMethods.Contains(context.Method) && role != TokenRole.RoleAdmin)
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Permission denied. Administrator role required."));
            }

            context.UserState["Token"] = token;
        }
    }
}