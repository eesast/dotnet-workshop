using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LogAnalyzerClient.Models
{
    public sealed record LogFileItem(string FileName)
    {
        public override string ToString() => FileName;
    }

    public sealed record LogTableRow(
        string LineNo,
        string Timestamp,
        string PodName,
        string Severity,
        string EventType,
        string RequestId,
        string TargetService,
        string DurationMs,
        string Method,
        string Path,
        string StatusCode,
        string ExceptionName,
        string ExceptionMessage)
    {
        public static LogTableRow FromFields(IReadOnlyDictionary<string, string> fields)
        {
            return new LogTableRow(
                LineNo: fields.GetValueOrDefault("LineNo", ""),
                Timestamp: fields.GetValueOrDefault("Timestamp", ""),
                PodName: fields.GetValueOrDefault("PodName", ""),
                Severity: fields.GetValueOrDefault("Severity", ""),
                EventType: fields.GetValueOrDefault("EventType", ""),
                RequestId: fields.GetValueOrDefault("RequestId", ""),
                TargetService: fields.GetValueOrDefault("TargetService", ""),
                DurationMs: fields.GetValueOrDefault("DurationMs", ""),
                Method: fields.GetValueOrDefault("Method", ""),
                Path: fields.GetValueOrDefault("Path", ""),
                StatusCode: fields.GetValueOrDefault("StatusCode", ""),
                ExceptionName: fields.GetValueOrDefault("ExceptionName", ""),
                ExceptionMessage: fields.GetValueOrDefault("ExceptionMessage", ""));
        }
    }
}
