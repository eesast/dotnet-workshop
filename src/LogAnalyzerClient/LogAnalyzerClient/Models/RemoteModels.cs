using LogAnalyzerRpc.Protos;
using System;

namespace LogAnalyzerClient.Models;

public sealed record LogFileItem(string FileName)
{
    public override string ToString() => FileName;
}

public sealed record FilterOption(string Label, string? Value);

public sealed record LogTableRow(
    int LineNo,
    string Timestamp,
    string PodName,
    LogSeverityEnum Severity,
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
    public string SeverityText => Severity.ToString();
    public bool IsInfo => Severity == LogSeverityEnum.Info;
    public bool IsWarning => Severity == LogSeverityEnum.Warning;
    public bool IsError => Severity == LogSeverityEnum.Error;

    public static LogTableRow FromGrpc(LogEntryMessage message)
    {
        return message.EntryCase switch
        {
            LogEntryMessage.EntryOneofCase.CallLogEntry => new LogTableRow(
                message.CallLogEntry.LineNo,
                FormatTimestamp(message.CallLogEntry.Timestamp),
                message.CallLogEntry.PodName,
                message.CallLogEntry.Severity,
                "Call",
                message.CallLogEntry.RequestId,
                message.CallLogEntry.TargetService,
                message.CallLogEntry.DurationMs.ToString(),
                "", "", "", "", ""),
            LogEntryMessage.EntryOneofCase.RequestLogEntry => new LogTableRow(
                message.RequestLogEntry.LineNo,
                FormatTimestamp(message.RequestLogEntry.Timestamp),
                message.RequestLogEntry.PodName,
                message.RequestLogEntry.Severity,
                "Request",
                message.RequestLogEntry.RequestId,
                "", "",
                message.RequestLogEntry.Method,
                message.RequestLogEntry.Path,
                message.RequestLogEntry.StatusCode.ToString(),
                "", ""),
            LogEntryMessage.EntryOneofCase.InternalLogEntry => new LogTableRow(
                message.InternalLogEntry.LineNo,
                FormatTimestamp(message.InternalLogEntry.Timestamp),
                message.InternalLogEntry.PodName,
                message.InternalLogEntry.Severity,
                "Internal",
                "", "", "", "", "", "",
                message.InternalLogEntry.ExceptionName,
                message.InternalLogEntry.ExceptionMessage),
            _ => throw new ArgumentException(
                $"Unknown log entry type: {message.EntryCase}.",
                nameof(message)),
        };
    }

    private static string FormatTimestamp(Google.Protobuf.WellKnownTypes.Timestamp value)
    {
        return value.ToDateTimeOffset().ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
    }
}
