using LogParser.Models;
using LogParser.Visitors;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LogAnalyzerClient.Models
{
    /// <summary>
    /// A flat, display-oriented row model backed by a single parsed log entry.
    /// Columns that do not apply to the entry type (e.g. <c>Method</c> on a
    /// <see cref="CallLogEntry"/>) are rendered as empty strings.
    /// </summary>
    public sealed class LogEntryRow
    {
        private static readonly KeyValueVisitor Visitor = new();

        public LogEntryRow(
            int lineNo,
            string timestamp,
            string podName,
            string severity,
            string eventType,
            string requestId,
            string targetService,
            string durationMs,
            string method,
            string path,
            string statusCode,
            string exceptionName,
            string exceptionMessage)
        {
            LineNo = lineNo;
            Timestamp = timestamp;
            PodName = podName;
            Severity = severity;
            EventType = eventType;
            RequestId = requestId;
            TargetService = targetService;
            DurationMs = durationMs;
            Method = method;
            Path = path;
            StatusCode = statusCode;
            ExceptionName = exceptionName;
            ExceptionMessage = exceptionMessage;
        }

        public int LineNo { get; }

        /// <summary>Raw ISO 8601 timestamp string (used as the sort key).</summary>
        public string Timestamp { get; }

        public string PodName { get; }

        public string Severity { get; }

        public string EventType { get; }

        public string RequestId { get; }

        public string TargetService { get; }

        public string DurationMs { get; }

        public string Method { get; }

        public string Path { get; }

        public string StatusCode { get; }

        public string ExceptionName { get; }

        public string ExceptionMessage { get; }

        public static LogEntryRow FromEntry(LogEntry entry)
        {
            var kv = Visitor.Dump(entry);
            string Get(string key) => kv.TryGetValue(key, out var value) ? value : string.Empty;
            return new LogEntryRow(
                lineNo: int.TryParse(Get("LineNo"), out var lineNo) ? lineNo : -1,
                timestamp: Get("Timestamp"),
                podName: Get("PodName"),
                severity: Get("Severity"),
                eventType: Get("EventType"),
                requestId: Get("RequestId"),
                targetService: Get("TargetService"),
                durationMs: Get("DurationMs"),
                method: Get("Method"),
                path: Get("Path"),
                statusCode: Get("StatusCode"),
                exceptionName: Get("ExceptionName"),
                exceptionMessage: Get("ExceptionMessage"));
        }
    }

    /// <summary>
    /// Builds the comparer used by the GUI "sort by" feature. Any column key listed in
    /// <see cref="Keys"/> can be sorted either ascending or descending.
    /// </summary>
    public static class LogRowSort
    {
        public static IReadOnlyList<string> Keys { get; } = new[]
        {
            "LineNo", "Timestamp", "PodName", "Severity", "EventType", "RequestId",
            "TargetService", "DurationMs", "Method", "Path", "StatusCode",
            "ExceptionName", "ExceptionMessage",
        };

        public static IComparer<LogEntryRow> CreateComparer(string sortKey, bool descending)
        {
            var comparer = Comparer<LogEntryRow>.Create((a, b) => descending
                ? CompareByKey(b, a, sortKey)
                : CompareByKey(a, b, sortKey));
            return comparer;
        }

        private static int CompareByKey(LogEntryRow a, LogEntryRow b, string key)
        {
            return key switch
            {
                "LineNo" => a.LineNo.CompareTo(b.LineNo),
                "Severity" => SeverityRank(a.Severity).CompareTo(SeverityRank(b.Severity)),
                "DurationMs" => CompareNullableInt(a.DurationMs, b.DurationMs),
                "StatusCode" => CompareNullableInt(a.StatusCode, b.StatusCode),
                "Timestamp" => CompareTimestamp(a.Timestamp, b.Timestamp),
                "EventType" => string.CompareOrdinal(a.EventType, b.EventType),
                _ => string.Compare(GetValue(a, key), GetValue(b, key), StringComparison.OrdinalIgnoreCase),
            };
        }

        private static string GetValue(LogEntryRow row, string key) => key switch
        {
            "LineNo" => row.LineNo.ToString(CultureInfo.InvariantCulture),
            "Timestamp" => row.Timestamp,
            "PodName" => row.PodName,
            "Severity" => row.Severity,
            "EventType" => row.EventType,
            "RequestId" => row.RequestId,
            "TargetService" => row.TargetService,
            "DurationMs" => row.DurationMs,
            "Method" => row.Method,
            "Path" => row.Path,
            "StatusCode" => row.StatusCode,
            "ExceptionName" => row.ExceptionName,
            "ExceptionMessage" => row.ExceptionMessage,
            _ => string.Empty,
        };

        private static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
        {
            "info" => 0,
            "warning" => 1,
            "error" => 2,
            _ => int.MaxValue,
        };

        private static int CompareNullableInt(string x, string y)
        {
            var xParsed = int.TryParse(x, out var xValue);
            var yParsed = int.TryParse(y, out var yValue);
            if (xParsed && yParsed)
            {
                return xValue.CompareTo(yValue);
            }
            if (xParsed != yParsed)
            {
                return xParsed ? 1 : -1;
            }
            return string.CompareOrdinal(x, y);
        }

        private static int CompareTimestamp(string x, string y)
        {
            if (DateTimeOffset.TryParse(x, CultureInfo.InvariantCulture, DateTimeStyles.None, out var xTime)
                && DateTimeOffset.TryParse(y, CultureInfo.InvariantCulture, DateTimeStyles.None, out var yTime))
            {
                return xTime.CompareTo(yTime);
            }
            return string.CompareOrdinal(x, y);
        }
    }
}
