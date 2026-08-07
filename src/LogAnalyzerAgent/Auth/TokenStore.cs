using System.Security.Cryptography;

namespace LogAnalyzerAgent.Auth
{
    /// <summary>
    /// Token 权限等级（T5.1.a.b）。
    /// 与 gRPC 的 <c>TokenRoleEnum</c> 一一对应，但属于 Agent 的领域模型，避免在业务逻辑中直接依赖生成代码。
    /// </summary>
    public enum TokenRole
    {
        /// <summary>普通权限：可进行各项日志分析操作。</summary>
        Normal,

        /// <summary>管理员权限：在普通权限基础上，可对其他 token 进行增删与权限调整。</summary>
        Admin,
    }

    /// <summary>
    /// 一个已签发的 token 的信息。Role / Note 在运行期可被管理员修改，故为可变类，
    /// 但所有读写都经由 <see cref="TokenStore"/> 在同一把锁下进行，保证线程安全。
    /// </summary>
    public sealed class TokenInfo
    {
        public string Token { get; }
        public TokenRole Role { get; set; }
        public string Note { get; set; }

        public TokenInfo(string token, TokenRole role, string note)
        {
            Token = token;
            Role = role;
            Note = note ?? "";
        }

        /// <summary>
        /// 复制一份，供 <see cref="TokenStore.List"/> 返回不可变快照使用，避免调用方持有可变引用。
        /// </summary>
        public TokenInfo Clone() => new(Token, Role, Note);
    }

    /// <summary>
    /// 维护所有已签发 token 的存储（T5.1.a.b）。
    ///
    /// 职责：
    /// <list type="bullet">
    ///   <item>启动时生成一个管理员 token（<see cref="CreateAdminToken"/>）。</item>
    ///   <item>校验请求携带的 token（<see cref="TryGet"/>）。</item>
    ///   <item>管理员的增 / 删 / 列 / 改权限操作。</item>
    /// </list>
    ///
    /// 线程安全：所有公开方法都在同一把 <c>_lock</c> 下完成「检查 + 修改」的复合操作，
    /// 因此「不能删除 / 降级最后一个管理员」的防锁死策略是原子的。
    /// </summary>
    public sealed class TokenStore
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, TokenInfo> _tokens = new();

        /// <summary>
        /// 启动时签发一个管理员 token 并返回，供 Program.cs 通过 logger 输出。
        /// 该方法在 Agent 生命周期内只应调用一次。
        /// </summary>
        public string CreateAdminToken()
        {
            var info = new TokenInfo(GenerateToken(), TokenRole.Admin, "bootstrap admin token");
            lock (_lock)
            {
                _tokens[info.Token] = info;
            }
            return info.Token;
        }

        /// <summary>
        /// 校验 token 合法性。返回 null 表示不存在 / 非法。
        /// </summary>
        public TokenInfo? TryGet(string? token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }
            lock (_lock)
            {
                return _tokens.TryGetValue(token!, out var info) ? info : null;
            }
        }

        /// <summary>
        /// 签发一个新 token。
        /// </summary>
        public TokenInfo CreateToken(TokenRole role, string note)
        {
            var info = new TokenInfo(GenerateToken(), role, note);
            lock (_lock)
            {
                _tokens[info.Token] = info;
            }
            return info;
        }

        /// <summary>
        /// 删除一个 token。带防锁死策略：不允许删除最后一个管理员 token。
        /// 返回 (success, errorMessage)。
        /// </summary>
        public (bool success, string? error) TryDelete(string token)
        {
            lock (_lock)
            {
                if (!_tokens.TryGetValue(token, out var info))
                {
                    return (false, "Token does not exist.");
                }
                if (info.Role == TokenRole.Admin && AdminCountNoLock() <= 1)
                {
                    return (false, "Cannot delete the last remaining admin token (would lock out management).");
                }
                _tokens.Remove(token);
                return (true, null);
            }
        }

        /// <summary>
        /// 调整一个 token 的权限。带防锁死策略：不允许把最后一个管理员降级为普通权限。
        /// </summary>
        public (bool success, string? error) TrySetRole(string token, TokenRole role)
        {
            lock (_lock)
            {
                if (!_tokens.TryGetValue(token, out var info))
                {
                    return (false, "Token does not exist.");
                }
                if (info.Role == role)
                {
                    return (true, null);
                }
                if (info.Role == TokenRole.Admin && role == TokenRole.Normal && AdminCountNoLock() <= 1)
                {
                    return (false, "Cannot demote the last remaining admin token (would lock out management).");
                }
                info.Role = role;
                return (true, null);
            }
        }

        /// <summary>
        /// 列出所有 token（返回快照副本）。同时返回当前管理员数量供上层提示。
        /// </summary>
        public IReadOnlyList<TokenInfo> List()
        {
            lock (_lock)
            {
                return _tokens.Values.Select(t => t.Clone()).ToList();
            }
        }

        private int AdminCountNoLock()
        {
            return _tokens.Values.Count(t => t.Role == TokenRole.Admin);
        }

        /// <summary>
        /// 生成一个高熵、URL 安全的随机 token：24 字节密码学随机数 → base64url。
        /// </summary>
        private static string GenerateToken()
        {
            Span<byte> bytes = stackalloc byte[24];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
