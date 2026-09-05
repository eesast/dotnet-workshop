using LogParser.Models;
using System;

namespace LogAnalyzerClient.Models
{
    public sealed record LogFileItem(string FileName)
    {
        public override string ToString() => FileName;
    }

    public sealed record LogEntryRow(
        int LineNo,
        DateTimeOffset Timestamp,
        string PodName,
        LogSeverity Severity,
        LogEventType EventType,
        string? RequestId,
        string? TargetService,
        int? DurationMs,
        string? Method,
        string? Path,
        int? StatusCode,
        string? ExceptionName,
        string? ExceptionMessage)
    {
        public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");

        public bool IsInfo => Severity == LogSeverity.Info;

        public bool IsWarning => Severity == LogSeverity.Warning;

        public bool IsError => Severity == LogSeverity.Error;

        public static LogEntryRow FromLogEntry(LogEntry entry)
        {
            return entry switch
            {
                CallLogEntry call => new LogEntryRow(
                    call.LineNo,
                    call.Timestamp,
                    call.PodName,
                    call.Severity,
                    call.EventType,
                    call.RequestId,
                    call.TargetService,
                    call.DurationMs,
                    null,
                    null,
                    null,
                    null,
                    null),
                RequestLogEntry request => new LogEntryRow(
                    request.LineNo,
                    request.Timestamp,
                    request.PodName,
                    request.Severity,
                    request.EventType,
                    request.RequestId,
                    null,
                    null,
                    request.Method,
                    request.Path,
                    request.StatusCode,
                    null,
                    null),
                InternalLogEntry internalEntry => new LogEntryRow(
                    internalEntry.LineNo,
                    internalEntry.Timestamp,
                    internalEntry.PodName,
                    internalEntry.Severity,
                    internalEntry.EventType,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    internalEntry.ExceptionName,
                    internalEntry.ExceptionMessage),
                _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, null)
            };
        }
    }
}
