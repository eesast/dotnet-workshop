using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LogAnalyzerClient.Models
{
    public sealed record LogFileItem(string FileName)
    {
        public override string ToString() => FileName;
    }

    public sealed record LogFields(int Index, IReadOnlyList<LogFieldItem> Fields, string? ErrorMessage)
    {
        public string Summary => GetMessage();
        private string GetMessage() => Fields switch
        {
            null or [] => ErrorMessage ?? "No fields",
            _ => Fields.FirstOrDefault(f => f.Key == "Type")?.Value switch
            {
                "Header" => $"Header: {GetFieldValue("FileName")} - {GetFieldValue("State")}\n{GetFieldValue("ErrorMessage", ErrorMessage ?? string.Empty)}",
                "LogEntry" => $"Log Entry: {string.Join(", ", Fields.Where(f => f.Key != "Type").Select(f => $"{f.Key}={f.Value}"))}",
                "Error" => $"Error: {GetFieldValue("Code")}\n{GetFieldValue("Message", ErrorMessage ?? "Unknown error")}",
                _ => "Unknown type"
            }
        };

        private string GetFieldValue(string key, string defaultValue = "N/A")
        {
            return Fields.FirstOrDefault(f => f.Key == key)?.Value ?? defaultValue;
        }

    }

    public sealed record LogFieldItem(string Key, string Value);
}
