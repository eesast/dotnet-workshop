using LogParser.Models;

namespace LogParser.Visitors
{
    public class ParquetVisitor : ILogEntryVisitor<ParquetLogEntry>
    {
        public ParquetLogEntry Dump(LogEntry entry)
        {
            return entry.Accept(this);
        }

        public ParquetLogEntry Visit(CallLogEntry entry)
        {
            return new ParquetLogEntry
            {
                LineNo = entry.LineNo,
                Timestamp = entry.Timestamp.UtcDateTime,
                PodName = entry.PodName,
                Severity = entry.Severity.ToString(),
                EventType = entry.EventType.ToString(),
                RequestId = entry.RequestId,
                TargetService = entry.TargetService,
                DurationMs = entry.DurationMs,
            };
        }

        public ParquetLogEntry Visit(RequestLogEntry entry)
        {
            return new ParquetLogEntry
            {
                LineNo = entry.LineNo,
                Timestamp = entry.Timestamp.UtcDateTime,
                PodName = entry.PodName,
                Severity = entry.Severity.ToString(),
                EventType = entry.EventType.ToString(),
                RequestId = entry.RequestId,
                Method = entry.Method,
                Path = entry.Path,
                StatusCode = entry.StatusCode,
            };
        }

        public ParquetLogEntry Visit(InternalLogEntry entry)
        {
            return new ParquetLogEntry
            {
                LineNo = entry.LineNo,
                Timestamp = entry.Timestamp.UtcDateTime,
                PodName = entry.PodName,
                Severity = entry.Severity.ToString(),
                EventType = entry.EventType.ToString(),
                ExceptionName = entry.ExceptionName,
                ExceptionMessage = entry.ExceptionMessage,
            };
        }
    }
}