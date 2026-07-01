using LogParser.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogParser.Parser
{
    internal static class LineParser
    {
        public static LogEntry ParseLine(LogRecord logRecord)
        {
            using (var doc = JsonDocument.Parse(logRecord.Message))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("event", out var eventElement))
                {
                    return eventElement.GetString() switch
                    {
                        "call" => LineParser.CreateCall(logRecord),
                        "request" => throw new NotImplementedException("TODO: T1.2"),
                        "internal" => throw new NotImplementedException("TODO: T1.2"),
                        _ => throw new FormatException($"Unknown event type: {eventElement.GetString()} in log message: {logRecord.Message}")
                    };
                }
                else
                {
                    throw new FormatException($"Log message does not contain 'event' property: {logRecord.Message}");
                }
            }
        }

        private static JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        };

        private static LogEntry CreateCall(LogRecord logRecord)
        {
            var callMessage = JsonSerializer.Deserialize<CallMessage>(logRecord.Message, options)
                ?? throw new FormatException($"Failed to deserialize call message: {logRecord.Message}");
            return new CallLogEntry(
                LineNo: logRecord.LineNo,
                Timestamp: DateTimeOffset.Parse(logRecord.Timestamp),
                PodName: logRecord.PodName,
                Severity: ParseSeverity(callMessage.Severity),
                RequestId: callMessage.RequestId,
                TargetService: callMessage.TargetService,
                DurationMs: callMessage.DurationMs
            );
        }

        private static LogEntry CreateRequest(LogRecord logRecord)
        {
            throw new NotImplementedException("TODO: T1.2");
        }

        private static LogEntry CreateInternal(LogRecord logRecord)
        {
            throw new NotImplementedException("TODO: T1.2");
        }

        private static LogSeverity ParseSeverity(string severity)
        {
            return severity.ToLower() switch
            {
                "info" => LogSeverity.Info,
                "warning" => LogSeverity.Warning,
                "error" => LogSeverity.Error,
                _ => throw new FormatException($"Unknown severity level: {severity}")
            };
        }

        private record CallMessage(
            [property: JsonRequired] string Severity,
            [property: JsonRequired] string RequestId,
            [property: JsonRequired] string TargetService,
            [property: JsonRequired] int DurationMs
        );

        private record RequestMessage(
            // TODO: T1.2
        );

        private record InternalMessage(
            // TODO: T1.2
        );
    }
}
