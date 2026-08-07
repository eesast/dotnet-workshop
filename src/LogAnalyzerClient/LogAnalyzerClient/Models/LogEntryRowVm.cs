using System;
using System.Text.RegularExpressions;
using LogParser.Models;

namespace LogAnalyzerClient.Models
{
    /// <summary>
    /// 分析结果表格中的单行视图模型（T5.1.b.a）。
    /// 把 <see cref="LogEntry"/> 的各字段拆成强类型列属性，便于 DataGrid 按列展示，
    /// 并让 Severity 列可通过 <c>SeverityToBrushConverter</c> 做颜色高亮。
    /// 相比旧的 <c>LogFields</c>「序号 + 键值列表 + 拼接字符串」，这种强类型模型更适合表格列绑定。
    /// </summary>
    public sealed class LogEntryRowVm
    {
        // 与 AgentSession.ExtractService 相同的规则：剥去 pod 副本后缀，gateway-0 -> gateway。
        private static readonly Regex PodSuffixRegex = new(@"-\d+$", RegexOptions.Compiled);

        public LogEntryRowVm(LogEntry entry)
        {
            Entry = entry;
            LineNo = entry.LineNo;
            Timestamp = entry.Timestamp;
            Time = entry.Timestamp.ToString("HH:mm:ss.fff");
            Severity = entry.Severity;
            EventType = entry.EventType.ToString();
            Service = ExtractService(entry.PodName);
            (RequestId, Detail) = BuildDetail(entry);
        }

        /// <summary>原始日志条目，供右键「追踪此 Request」等场景读取 RequestId 等字段。</summary>
        public LogEntry Entry { get; }

        public int LineNo { get; }

        /// <summary>原始时间戳，供排序 / 瀑布图取时间用。</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>格式化后的时间文本（表格 Time 列）。</summary>
        public string Time { get; }

        /// <summary>日志等级枚举，表格 Severity 列绑定它并经转换器着色。</summary>
        public LogSeverity Severity { get; }

        /// <summary>产生该条日志的服务（pod 副本后缀已剥除）。</summary>
        public string Service { get; }

        /// <summary>事件类型文本：Call / Request / Internal。</summary>
        public string EventType { get; }

        /// <summary>该条日志的 Request ID；Internal 类型为空字符串（无此字段）。</summary>
        public string RequestId { get; }

        /// <summary>按事件类型摘要的消息列。</summary>
        public string Detail { get; }

        /// <summary>剥去 pod 副本后缀的服务名；与 Agent 端拓扑聚合的 ExtractService 规则一致。</summary>
        public static string ExtractService(string podName) =>
            string.IsNullOrEmpty(podName) ? podName : PodSuffixRegex.Replace(podName, string.Empty);

        private static (string requestId, string detail) BuildDetail(LogEntry entry) => entry switch
        {
            CallLogEntry c => (c.RequestId, $"-> {c.TargetService} ({c.DurationMs} ms)"),
            RequestLogEntry r => (r.RequestId, $"{r.Method} {r.Path} -> {r.StatusCode}"),
            InternalLogEntry i => ("", $"{i.ExceptionName}: {i.ExceptionMessage}"),
            _ => ("", ""),
        };
    }
}
