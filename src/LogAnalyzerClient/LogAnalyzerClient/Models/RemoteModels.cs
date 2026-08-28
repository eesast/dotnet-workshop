using Avalonia.Media;
using LogParser.Models;

namespace LogAnalyzerClient.Models
{
    public sealed record LogFileItem(string FileName)
    {
        public override string ToString() => FileName;
    }

    public sealed class LogEntryRow
    {
        public string LineNo { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string PodName { get; set; } = "";
        public string Severity { get; set; } = "";
        public string EventType { get; set; } = "";
        public string RequestId { get; set; } = "";
        public string TargetService { get; set; } = "";
        public string DurationMs { get; set; } = "";
        public string Method { get; set; } = "";
        public string Path { get; set; } = "";
        public string StatusCode { get; set; } = "";
        public string ExceptionName { get; set; } = "";
        public string ExceptionMessage { get; set; } = "";
        public IBrush SeverityBrush { get; set; } = Brushes.Transparent;

        public static LogEntryRow From(LogEntry entry)
        {
            var row = new LogEntryRow
            {
                LineNo = entry.LineNo.ToString(),
                Timestamp = entry.Timestamp.ToString("O"),
                PodName = entry.PodName,
                Severity = entry.Severity.ToString(),
                EventType = entry.EventType.ToString(),
                SeverityBrush = entry.Severity switch
                {
                    LogSeverity.Info => new SolidColorBrush(Color.Parse("#2E8BFF")),
                    LogSeverity.Warning => new SolidColorBrush(Color.Parse("#F5A623")),
                    LogSeverity.Error => new SolidColorBrush(Color.Parse("#E5484D")),
                    _ => Brushes.Transparent,
                },
            };

            switch (entry)
            {
                case CallLogEntry call:
                    row.RequestId = call.RequestId;
                    row.TargetService = call.TargetService;
                    row.DurationMs = call.DurationMs.ToString();
                    break;
                case RequestLogEntry request:
                    row.RequestId = request.RequestId;
                    row.Method = request.Method;
                    row.Path = request.Path;
                    row.StatusCode = request.StatusCode.ToString();
                    break;
                case InternalLogEntry internalEntry:
                    row.ExceptionName = internalEntry.ExceptionName;
                    row.ExceptionMessage = internalEntry.ExceptionMessage;
                    break;
            }

            return row;
        }
    }
}
