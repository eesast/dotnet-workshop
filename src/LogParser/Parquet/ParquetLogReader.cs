using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LogParser.Models;
using Parquet.Serialization;

namespace LogParser.Parquet
{
    /// <summary>
    /// 读取 Parquet 日志文件（列 schema 见 <see cref="ParquetLogRow"/>），逐行还原为 <see cref="LogEntry"/>。
    /// 缺失的字段按该事件类型的默认值处理；无法识别的 event_type 行会抛出 <see cref="FormatException"/>。
    /// </summary>
    public static class ParquetLogReader
    {
        /// <summary>
        /// 读取 <paramref name="path"/> 指定的 Parquet 日志文件，按行号顺序返回日志条目。
        /// </summary>
        public static async Task<IReadOnlyList<LogEntry>> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            var entries = new List<LogEntry>();

            using Stream fs = File.OpenRead(path);
            // 用 Parquet.Net 的高级序列化 API 反序列化为 POCO，再逐行转换回领域模型 LogEntry。
            var result = await ParquetSerializer.DeserializeAsync<ParquetLogRow>(fs, null, null, cancellationToken);
            foreach (var row in result.Data)
            {
                if (row is null)
                {
                    continue;
                }
                entries.Add(ToEntry(row));
            }
            return entries;
        }

        private static LogEntry ToEntry(ParquetLogRow r)
        {
            int lineNo = r.LineNo;
            DateTimeOffset timestamp = DateTimeOffset.Parse(
                r.Timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            string podName = r.PodName ?? "";
            LogSeverity severity = ParseSeverity(r.Severity);

            return (r.EventType ?? "").ToLowerInvariant() switch
            {
                "call" => new CallLogEntry(
                    LineNo: lineNo,
                    Timestamp: timestamp,
                    PodName: podName,
                    Severity: severity,
                    RequestId: r.RequestId ?? "",
                    TargetService: r.TargetService ?? "",
                    DurationMs: r.DurationMs ?? 0),
                "request" => new RequestLogEntry(
                    LineNo: lineNo,
                    Timestamp: timestamp,
                    PodName: podName,
                    Severity: severity,
                    RequestId: r.RequestId ?? "",
                    Method: r.Method ?? "",
                    Path: r.Path ?? "",
                    StatusCode: r.StatusCode ?? 0),
                "internal" => new InternalLogEntry(
                    LineNo: lineNo,
                    Timestamp: timestamp,
                    PodName: podName,
                    Severity: severity,
                    ExceptionName: r.ExceptionName ?? "",
                    ExceptionMessage: r.ExceptionMessage ?? ""),
                _ => throw new FormatException($"Unknown event_type '{r.EventType}' in parquet row (line {lineNo})."),
            };
        }

        private static LogSeverity ParseSeverity(string? severity)
        {
            return (severity ?? "").ToLowerInvariant() switch
            {
                "info" => LogSeverity.Info,
                "warning" => LogSeverity.Warning,
                "error" => LogSeverity.Error,
                _ => throw new FormatException($"Unknown severity level: {severity}"),
            };
        }
    }
}
