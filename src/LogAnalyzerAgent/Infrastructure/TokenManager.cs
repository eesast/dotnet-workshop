using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerAgent.Infrastructure
{
    public class TokenInfoInternal
    {
        public string Token { get; set; } = string.Empty;
        public TokenRole Role { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TokenManager
    {
        private readonly ConcurrentDictionary<string, TokenInfoInternal> _tokens = new();
        private readonly ILogger<TokenManager> _logger;

        public TokenManager(ILogger<TokenManager> logger)
        {
            _logger = logger;

            // 1. 启动时生成初始 Admin Token 并通过 _logger 输出
            var initialAdminToken = Guid.NewGuid().ToString("N");
            _tokens[initialAdminToken] = new TokenInfoInternal
            {
                Token = initialAdminToken,
                Role = TokenRole.RoleAdmin,
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogWarning("==================================================");
            _logger.LogWarning("[Admin Token Generated] Initial Admin Token: {Token}", initialAdminToken);
            _logger.LogWarning("==================================================");
        }

        public bool ValidateToken(string token, out TokenRole role)
        {
            if (!string.IsNullOrEmpty(token) && _tokens.TryGetValue(token, out var info))
            {
                role = info.Role;
                return true;
            }
            role = TokenRole.RoleUnspecified;
            return false;
        }

        public TokenInfoInternal CreateToken(TokenRole role)
        {
            var token = Guid.NewGuid().ToString("N");
            var info = new TokenInfoInternal
            {
                Token = token,
                Role = role,
                CreatedAt = DateTime.UtcNow
            };
            _tokens[token] = info;
            return info;
        }

        public bool RevokeToken(string token, string currentActiveToken)
        {
            // 禁止注销当前正在使用的 Token，防止管理员误把自己锁出
            if (token == currentActiveToken) return false;
            return _tokens.TryRemove(token, out _);
        }

        public IEnumerable<TokenInfoInternal> ListTokens() => _tokens.Values;
    }
}