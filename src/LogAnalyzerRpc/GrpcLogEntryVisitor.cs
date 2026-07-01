using Google.Protobuf.WellKnownTypes;
using LogAnalyzerRpc.Protos;
using LogParser.Models;

namespace LogAnalyzerRpc
{
    internal class GrpcLogEntryVisitor : ILogEntryVisitor<LogEntryMessage>
    {
        public static GrpcLogEntryVisitor Instance { get; } = new();

        private GrpcLogEntryVisitor() { }

        public LogEntryMessage Visit(CallLogEntry entry)
        {
            return new LogEntryMessage()
            {
                CallLogEntry = new CallLogEntryMessage
                {
                    LineNo = entry.LineNo,
                    Timestamp = Timestamp.FromDateTimeOffset(entry.Timestamp),
                    PodName = entry.PodName,
                    Severity = GrpcTypeConverter.ConvertToGrpc(entry.Severity),
                    EventType = GrpcTypeConverter.ConvertToGrpc(entry.EventType),
                    RequestId = entry.RequestId,
                    TargetService = entry.TargetService,
                    DurationMs = entry.DurationMs,
                }
            };
        }

        public LogEntryMessage Visit(RequestLogEntry entry)
        {
            throw new NotImplementedException("TODO: T3.1");
        }

        public LogEntryMessage Visit(InternalLogEntry entry)
        {
            throw new NotImplementedException("TODO: T3.1");
        }
    }
}
