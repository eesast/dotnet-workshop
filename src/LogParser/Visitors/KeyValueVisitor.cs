using LogParser.Models;

namespace LogParser.Visitors
{
    public class KeyValueVisitor : ILogEntryVisitor<Dictionary<string, string>>
    {
        public Dictionary<string, string> Dump(LogEntry entry)
        {
            return entry.Accept(this);
        }

        public Dictionary<string, string> Visit(CallLogEntry entry)
        {
            return new Dictionary<string, string>
            {
                ["LineNo"] = entry.LineNo.ToString(),
                ["Timestamp"] = entry.Timestamp.ToString("O"),
                ["PodName"] = entry.PodName,
                ["Severity"] = entry.Severity.ToString(),
                ["EventType"] = entry.EventType.ToString(),
                ["RequestId"] = entry.RequestId,
                ["TargetService"] = entry.TargetService,
                ["DurationMs"] = entry.DurationMs.ToString(),
            };
        }

        public Dictionary<string, string> Visit(RequestLogEntry entry)
        {
            throw new NotImplementedException("TODO: T1.3");
        }

        public Dictionary<string, string> Visit(InternalLogEntry entry)
        {
            throw new NotImplementedException("TODO: T1.3");
        }
    }
}
