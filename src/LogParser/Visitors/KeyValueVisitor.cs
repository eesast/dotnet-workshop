using LogParser.Models;
using System.Collections.Generic;

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

         // 新增：处理Request类型日志
        public Dictionary<string, string> Visit(RequestLogEntry entry)
        {
            return new Dictionary<string, string>
            {
                ["LineNo"] = entry.LineNo.ToString(),
                ["Timestamp"] = entry.Timestamp.ToString("O"),
                ["PodName"] = entry.PodName,
                ["Severity"] = entry.Severity.ToString(),
                ["EventType"] = entry.EventType.ToString(),
                ["RequestId"] = entry.RequestId,
                ["Method"] = entry.Method,
                ["Path"] = entry.Path,
                ["StatusCode"] = entry.StatusCode.ToString()
            };
        }

        // 新增：处理 Internal 类型日志
        public Dictionary<string, string> Visit(InternalLogEntry entry)
        {
            return new Dictionary<string, string>
            {
                ["LineNo"] = entry.LineNo.ToString(),
                ["Timestamp"] = entry.Timestamp.ToString("O"),
                ["PodName"] = entry.PodName,
                ["Severity"] = entry.Severity.ToString(),
                ["EventType"] = entry.EventType.ToString(),
                ["ExceptionName"] = entry.ExceptionName,
                ["ExceptionMessage"] = entry.ExceptionMessage
            };
        }
    }
}
