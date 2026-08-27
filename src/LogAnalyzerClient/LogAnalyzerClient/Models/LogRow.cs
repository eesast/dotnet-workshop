using Avalonia.Media;
using LogParser.Models;
using System;
using System.Linq;

namespace LogAnalyzerClient.Models
{
    /// <summary>
    /// 日志表格的一行（T5.1.b.a 表格化显示）。把不同类型的日志
    /// 摊平到统一的一组列中，不适用于当前类型的列留空。
    /// </summary>
    public sealed record LogRow
    {
        public int LineNo { get; init; }
        public string Timestamp { get; init; } = string.Empty;
        public string PodName { get; init; } = string.Empty;
        public string Service { get; init; } = string.Empty;
        public string SeverityText { get; init; } = string.Empty;
        public IBrush SeverityBrush { get; init; } = Brushes.Transparent;
        public string EventType { get; init; } = string.Empty;
        public string RequestId { get; init; } = string.Empty;
        public string TargetService { get; init; } = string.Empty;
        public string DurationMs { get; init; } = string.Empty;
        public string Method { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string StatusCode { get; init; } = string.Empty;
        public string ExceptionName { get; init; } = string.Empty;
        public string ExceptionMessage { get; init; } = string.Empty;

        /// <summary>从 Pod 名称提取服务名，例如 "gateway-0" -> "gateway"。</summary>
        public static string ServiceOf(string podName)
        {
            if (string.IsNullOrEmpty(podName))
            {
                return podName;
            }
            int idx = podName.LastIndexOf('-');
            if (idx > 0 && idx < podName.Length - 1 && podName[(idx + 1)..].All(char.IsDigit))
            {
                return podName[..idx];
            }
            return podName;
        }

        public static LogRow FromEntry(LogEntry entry)
        {
            var row = new LogRow
            {
                LineNo = entry.LineNo,
                Timestamp = entry.Timestamp.ToString("O"),
                PodName = entry.PodName,
                Service = ServiceOf(entry.PodName),
                SeverityText = entry.Severity.ToString(),
                SeverityBrush = SeverityToBrush(entry.Severity),
                EventType = entry.EventType.ToString(),
            };

            switch (entry)
            {
                case CallLogEntry call:
                    return row with
                    {
                        RequestId = call.RequestId,
                        TargetService = call.TargetService,
                        DurationMs = call.DurationMs.ToString(),
                    };
                case RequestLogEntry request:
                    return row with
                    {
                        RequestId = request.RequestId,
                        Method = request.Method,
                        Path = request.Path,
                        StatusCode = request.StatusCode.ToString(),
                    };
                case InternalLogEntry internalEntry:
                    return row with
                    {
                        ExceptionName = internalEntry.ExceptionName,
                        ExceptionMessage = internalEntry.ExceptionMessage,
                    };
                default:
                    return row;
            }
        }

        private static IBrush SeverityToBrush(LogSeverity severity)
        {
            return severity switch
            {
                LogSeverity.Info => new SolidColorBrush(Color.Parse("#3B82F6")),     // 蓝色
                LogSeverity.Warning => new SolidColorBrush(Color.Parse("#F59E0B")), // 橙色
                LogSeverity.Error => new SolidColorBrush(Color.Parse("#EF4444")),   // 红色
                _ => Brushes.Transparent,
            };
        }
    }
}
