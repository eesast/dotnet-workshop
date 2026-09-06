using Parquet.Serialization.Attributes;

namespace LogParser.Models
{
    public sealed class ParquetLogEntry
    {
        public int LineNo { get; set; }
        public DateTime Timestamp { get; set; }
        [ParquetRequired]
        public string PodName { get; set; } = "";
        [ParquetRequired]
        public string Severity { get; set; } = "";
        [ParquetRequired]
        public string EventType { get; set; } = "";

        public string? RequestId { get; set; }

        public string? TargetService { get; set; }
        public int? DurationMs { get; set; }

        public string? Method { get; set; }
        public string? Path { get; set; }
        public int? StatusCode { get; set; }

        public string? ExceptionName { get; set; }
        public string? ExceptionMessage { get; set; }

        public LogEntry ToLogEntry()
        {
            var severity = Severity.ToLower() switch
            {
                "info" => LogSeverity.Info,
                "warning" => LogSeverity.Warning,
                "error" => LogSeverity.Error,
                _ => throw new FormatException($"Unknown severity level: {Severity}")
            };

            return EventType.ToLower() switch
            {
                "call" => new CallLogEntry(
                    LineNo: LineNo,
                    Timestamp: Timestamp,
                    PodName: PodName,
                    Severity: severity,
                    RequestId: RequestId ?? throw new FormatException("RequestId is required for CallLogEntry"),
                    TargetService: TargetService ?? throw new FormatException("TargetService is required for CallLogEntry"),
                    DurationMs: DurationMs ?? throw new FormatException("DurationMs is required for CallLogEntry")
                ),
                "request" => new RequestLogEntry(
                    LineNo: LineNo,
                    Timestamp: Timestamp,
                    PodName: PodName,
                    Severity: severity,
                    RequestId: RequestId ?? throw new FormatException("RequestId is required for RequestLogEntry"),
                    Method: Method ?? throw new FormatException("Method is required for RequestLogEntry"),
                    Path: Path ?? throw new FormatException("Path is required for RequestLogEntry"),
                    StatusCode: StatusCode ?? throw new FormatException("StatusCode is required for RequestLogEntry")
                ),
                "internal" => new InternalLogEntry(
                    LineNo: LineNo,
                    Timestamp: Timestamp,
                    PodName: PodName,
                    Severity: severity,
                    ExceptionName: ExceptionName ?? throw new FormatException("ExceptionName is required for InternalLogEntry"),
                    ExceptionMessage: ExceptionMessage ?? throw new FormatException("ExceptionMessage is required for InternalLogEntry")
                ),
                _ => throw new FormatException($"Unknown event type: {EventType}")
            };
        }
    }
}