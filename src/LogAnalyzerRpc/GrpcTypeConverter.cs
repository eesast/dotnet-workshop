using Google.Protobuf.WellKnownTypes;
using LogAnalyzer;
using LogAnalyzerRpc.Protos;
using LogParser.Models;

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
            throw new NotImplementedException("TODO: T3.1");
        }

        public static LogEventTypeEnum ConvertToGrpc(LogEventType eventType)
        {
            throw new NotImplementedException("TODO: T3.1");
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
            throw new NotImplementedException("TODO: T3.1");
        }

        public static LogEventType ConvertFromGrpc(LogEventTypeEnum eventType)
        {
            throw new NotImplementedException("TODO: T3.1");
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
                LogEntryMessage.EntryOneofCase.RequestLogEntry => throw new NotImplementedException("TODO: T3.1"),
                LogEntryMessage.EntryOneofCase.InternalLogEntry => throw new NotImplementedException("TODO: T3.1"),
                _ => throw new ArgumentException($"Unknown entry type: {entryMessage.EntryCase}", nameof(entryMessage))
            };
        }
    }
}
