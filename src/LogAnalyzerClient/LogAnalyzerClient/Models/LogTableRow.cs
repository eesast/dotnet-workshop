namespace LogAnalyzerClient.Models;

public sealed record LogTableRow(
    int LineNo,
    string Timestamp,
    string PodName,
    string Severity,
    string EventType,
    string RequestId,
    string TargetService,
    string Method,
    string Path,
    string StatusCode,
    string DurationMs,
    string ExceptionName,
    string ExceptionMessage);