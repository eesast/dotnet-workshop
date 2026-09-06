using Grpc.Core;
using System.Collections.Concurrent;

namespace LogAnalyzerAgent.Applications
{
    public class AgentSessionRegistry
    {
        private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
        private readonly ILoggerFactory _loggerFactory;
        private readonly string[] _validTokens;

        public AgentSessionRegistry(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _validTokens = configuration.GetSection("AgentAuth:Tokens").Get<string[]>() ?? [];
            _loggerFactory = loggerFactory;
        }

        public AgentSession GetOrCreateSession(ServerCallContext context)
        {
            var authorization = context.RequestHeaders.GetValue("authorization");
            if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Authorization header is missing or malformed."));
            }

            var token = authorization["Bearer ".Length..].Trim();
            if (string.IsNullOrEmpty(token))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Bearer token is empty."));
            }

            return GetOrCreateSession(token);
        }

        public AgentSession GetOrCreateSession(string token)
        {
            if (!_validTokens.Contains(token))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid token."));
            }
            return _sessions.GetOrAdd(token, _ => new AgentSession(new LogAnalyzer.LogFileAnalyzer(), _loggerFactory));
        }

        public void RemoveSession(string token)
        {
            _sessions.TryRemove(token, out _);
        }
    }
}