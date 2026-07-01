namespace LogParser.Models
{
    public enum LogSeverity
    {
        Info,
        Warning,
        Error,
    }

    public enum LogEventType
    {
        Call,
        Request,
        Internal
    }

    public abstract record LogEntry(
        int LineNo,
        DateTimeOffset Timestamp,
        string PodName,
        LogSeverity Severity,
        LogEventType EventType
    )
    {
        public abstract TResult Accept<TResult>(ILogEntryVisitor<TResult> visitor);
    }

    public sealed record CallLogEntry(
        int LineNo,
        DateTimeOffset Timestamp,
        string PodName,
        LogSeverity Severity,
        string RequestId,
        string TargetService,
        int DurationMs
    ) : LogEntry(LineNo, Timestamp, PodName, Severity, LogEventType.Call)
    {
        public override TResult Accept<TResult>(ILogEntryVisitor<TResult> visitor)
        {
            return visitor.Visit(this);
        }
    }

    public sealed record RequestLogEntry(
        int LineNo,
        DateTimeOffset Timestamp,
        string PodName,
        LogSeverity Severity,
        string RequestId,
        string Method,
        string Path,
        int StatusCode)
        : LogEntry(LineNo, Timestamp, PodName, Severity, LogEventType.Request)
    {
        public override TResult Accept<TResult>(ILogEntryVisitor<TResult> visitor)
        {
            throw new NotImplementedException("TODO: T1.2");
        }
    }

    public sealed record InternalLogEntry(
        int LineNo,
        DateTimeOffset Timestamp,
        string PodName,
        LogSeverity Severity,
        string ExceptionName,
        string ExceptionMessage)
        : LogEntry(LineNo, Timestamp, PodName, Severity, LogEventType.Internal)
    {
        public override TResult Accept<TResult>(ILogEntryVisitor<TResult> visitor)
        {
            throw new NotImplementedException("TODO: T1.2");
        }
    }

}
