namespace LogParser.Parquet
{
    /// <summary>
    /// Parquet 日志文件中一行记录的 POCO 表示，也是 Parquet.Net 序列化时所依据的「列 schema」。
    ///
    /// 由于 Call / Request / Internal 三种日志的字段各不相同，我们采用「宽表」设计：
    /// 把所有可能的列都放进同一张表，与某条日志无关的列在该行留空（null / 默认值）。
    /// 这正是 Parquet 这类列式存储擅长处理的场景——稀疏列的空值压缩代价很低。
    ///
    /// 公共列（LineNo / Timestamp / PodName / Severity / EventType）对所有日志都有值；
    /// 其余列按事件类型选择性填写。Parquet.Net 会把 <c>int?</c> / <c>string?</c> 自动处理为可空列。
    /// </summary>
    public sealed class ParquetLogRow
    {
        // —— 公共列（三种日志都有）——
        /// <summary>行号，即日志在原文件中的行序。</summary>
        public int LineNo { get; set; }

        /// <summary>时间戳，ISO 8601 字符串（round-trip "O" 格式），读回时用 <see cref="System.DateTimeOffset.Parse"/> 还原。</summary>
        public string Timestamp { get; set; } = "";

        /// <summary>产生日志的 pod 名，如 gateway-0。</summary>
        public string PodName { get; set; } = "";

        /// <summary>日志等级：info / warning / error。</summary>
        public string Severity { get; set; } = "";

        /// <summary>事件类型：call / request / internal。</summary>
        public string EventType { get; set; } = "";

        // —— Call / Request 共有 ——
        /// <summary>Request ID（Internal 日志无此字段）。</summary>
        public string? RequestId { get; set; }

        // —— Call 特有 ——
        /// <summary>被调用的目标服务。</summary>
        public string? TargetService { get; set; }

        /// <summary>调用耗时（毫秒）。</summary>
        public int? DurationMs { get; set; }

        // —— Request 特有 ——
        /// <summary>HTTP 方法，如 GET。</summary>
        public string? Method { get; set; }

        /// <summary>请求路径。</summary>
        public string? Path { get; set; }

        /// <summary>HTTP 状态码。</summary>
        public int? StatusCode { get; set; }

        // —— Internal 特有 ——
        /// <summary>异常名。</summary>
        public string? ExceptionName { get; set; }

        /// <summary>异常信息。</summary>
        public string? ExceptionMessage { get; set; }
    }
}
