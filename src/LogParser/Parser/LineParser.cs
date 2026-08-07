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
                        "request" => LineParser.CreateRequest(logRecord),
                        "internal" => LineParser.CreateInternal(logRecord),
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
            var requestMessage = JsonSerializer.Deserialize<RequestMessage>(logRecord.Message, options)
                ?? throw new FormatException($"Failed to deserialize request message: {logRecord.Message}");
            return new RequestLogEntry(
                LineNo: logRecord.LineNo,
                Timestamp: DateTimeOffset.Parse(logRecord.Timestamp),
                PodName: logRecord.PodName,
                Severity: ParseSeverity(requestMessage.Severity),
                RequestId: requestMessage.RequestId,
                Method: requestMessage.Method,
                Path: requestMessage.Path,
                StatusCode: requestMessage.StatusCode
            );
        }

        private static LogEntry CreateInternal(LogRecord logRecord)
        {
            var internalMessage = JsonSerializer.Deserialize<InternalMessage>(logRecord.Message, options)
                ?? throw new FormatException($"Failed to deserialize internal message: {logRecord.Message}");

            // exception 值的格式："ExceptionName: exception message"
            // 按 ": " 拆成两部分
            var parts = internalMessage.Exception.Split(": ", 2, StringSplitOptions.None);
            if (parts.Length != 2)
                throw new FormatException(
                    $"Invalid exception format: '{internalMessage.Exception}'. " +
                    "Expected format: 'ExceptionName: exception message'");

            return new InternalLogEntry(
                LineNo: logRecord.LineNo,
                Timestamp: DateTimeOffset.Parse(logRecord.Timestamp),
                PodName: logRecord.PodName,
                Severity: ParseSeverity(internalMessage.Severity),
                ExceptionName: parts[0],
                ExceptionMessage: parts[1]
            );
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
            [property: JsonRequired] string Severity,
            [property: JsonRequired] string RequestId,
            [property: JsonRequired] string Method,
            [property: JsonRequired] string Path,
            [property: JsonRequired] int StatusCode
        );

        private record InternalMessage(
            [property: JsonRequired] string Severity,
            [property: JsonRequired] string Exception
        );
    }
}
