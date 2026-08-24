using Google.Protobuf.WellKnownTypes;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogParser.Models;
using System;

namespace LogAnalyzerRpc
{
    public static class GrpcTypeConverter
    {
        public static AnalysisStateEnum ConvertToGrpc(AnalysisState state)
        {
            return state switch
            {
                AnalysisState.NotAnalyzed => AnalysisStateEnum.NotAnalyzed,
                AnalysisState.Succeeded => AnalysisStateEnum.Succeeded,
                AnalysisState.Failed => AnalysisStateEnum.Failed,
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
            };
        }

        public static LogSeverityEnum ConvertToGrpc(LogSeverity severity)
        {
            return severity switch
            {
                LogSeverity.Info => LogSeverityEnum.Info,
                LogSeverity.Warning => LogSeverityEnum.Warning,
                LogSeverity.Error => LogSeverityEnum.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
            };
        }

        public static LogEventTypeEnum ConvertToGrpc(LogEventType eventType)
        {
            return eventType switch
            {
                LogEventType.Call => LogEventTypeEnum.Call,
                LogEventType.Request => LogEventTypeEnum.Request,
                LogEventType.Internal => LogEventTypeEnum.Internal,
                _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null)
            };
        }

        public static LogEntryMessage ConvertToGrpc(LogEntry entry)
        {
            return entry.Accept(GrpcLogEntryVisitor.Instance);
        }

        public static AnalysisState ConvertFromGrpc(AnalysisStateEnum state)
        {
            return state switch
            {
                AnalysisStateEnum.NotAnalyzed => AnalysisState.NotAnalyzed,
                AnalysisStateEnum.Succeeded => AnalysisState.Succeeded,
                AnalysisStateEnum.Failed => AnalysisState.Failed,
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
            };
        }

        public static LogSeverity ConvertFromGrpc(LogSeverityEnum severity)
        {
            return severity switch
            {
                LogSeverityEnum.Info => LogSeverity.Info,
                LogSeverityEnum.Warning => LogSeverity.Warning,
                LogSeverityEnum.Error => LogSeverity.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
            };
        }

        public static LogEventType ConvertFromGrpc(LogEventTypeEnum eventType)
        {
            return eventType switch
            {
                LogEventTypeEnum.Call => LogEventType.Call,
                LogEventTypeEnum.Request => LogEventType.Request,
                LogEventTypeEnum.Internal => LogEventType.Internal,
                _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null)
            };
        }

        public static LogEntry ConvertFromGrpc(LogEntryMessage entryMessage)
        {
            return entryMessage.EntryCase switch
            {
                LogEntryMessage.EntryOneofCase.CallLogEntry => new CallLogEntry(
                    LineNo: entryMessage.CallLogEntry.LineNo,
                    Timestamp: entryMessage.CallLogEntry.Timestamp.ToDateTimeOffset(),
                    PodName: entryMessage.CallLogEntry.PodName,
                    Severity: ConvertFromGrpc(entryMessage.CallLogEntry.Severity),
                    RequestId:  entryMessage.CallLogEntry.RequestId,
                    TargetService: entryMessage.CallLogEntry.TargetService,
                    DurationMs: entryMessage.CallLogEntry.DurationMs
                ),
                LogEntryMessage.EntryOneofCase.RequestLogEntry => new RequestLogEntry(
                    LineNo: entryMessage.RequestLogEntry.LineNo,
                    Timestamp: entryMessage.RequestLogEntry.Timestamp.ToDateTimeOffset(),
                    PodName: entryMessage.RequestLogEntry.PodName,
                    Severity: ConvertFromGrpc(entryMessage.RequestLogEntry.Severity),
                    RequestId: entryMessage.RequestLogEntry.RequestId,
                    Method: entryMessage.RequestLogEntry.Method,
                    Path: entryMessage.RequestLogEntry.Path,
                    StatusCode: entryMessage.RequestLogEntry.StatusCode
                ),
                LogEntryMessage.EntryOneofCase.InternalLogEntry => new InternalLogEntry(
                    LineNo: entryMessage.InternalLogEntry.LineNo,
                    Timestamp: entryMessage.InternalLogEntry.Timestamp.ToDateTimeOffset(),
                    PodName: entryMessage.InternalLogEntry.PodName,
                    Severity: ConvertFromGrpc(entryMessage.InternalLogEntry.Severity),
                    ExceptionName: entryMessage.InternalLogEntry.ExceptionName,
                    ExceptionMessage: entryMessage.InternalLogEntry.ExceptionMessage
                ),
                _ => throw new ArgumentException($"Unknown entry type: {entryMessage.EntryCase}", nameof(entryMessage))
            };
        }
    }
}
