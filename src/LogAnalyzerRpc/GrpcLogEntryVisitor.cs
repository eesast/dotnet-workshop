using Google.Protobuf.WellKnownTypes;
using LogAnalyzerRpc.Protos;
using LogParser.Models;
using System.IO;

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
            return new LogEntryMessage()
            {
                RequestLogEntry = new RequestLogEntryMessage
                {
                    LineNo = entry.LineNo,
                    Timestamp = Timestamp.FromDateTimeOffset(entry.Timestamp),
                    PodName = entry.PodName,
                    Severity = GrpcTypeConverter.ConvertToGrpc(entry.Severity),
                    EventType = GrpcTypeConverter.ConvertToGrpc(entry.EventType),
                    RequestId = entry.RequestId,
                    Method = entry.Method,
                    Path = entry.Path,
                    StatusCode = entry.StatusCode,
                }
            };
        }

        public LogEntryMessage Visit(InternalLogEntry entry)
        {
            return new LogEntryMessage()
            {
                InternalLogEntry = new InternalLogEntryMessage
                {
                    LineNo = entry.LineNo,
                    Timestamp = Timestamp.FromDateTimeOffset(entry.Timestamp),
                    PodName = entry.PodName,
                    Severity = GrpcTypeConverter.ConvertToGrpc(entry.Severity),
                    EventType = GrpcTypeConverter.ConvertToGrpc(entry.EventType),
                    ExceptionName = entry.ExceptionName,
                    ExceptionMessage = entry.ExceptionMessage,
                }
            };
        }
    }
}
