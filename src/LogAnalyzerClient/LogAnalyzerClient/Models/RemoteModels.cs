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
        public string Summary => ErrorMessage is not null
            ? $"Analyze Failed, Error: {ErrorMessage}"
            : Fields.Count == 0
                ? "No fields Found"
                : $"#{Index} " + string.Join(", ", Fields.Select(f => $"{f.Key}: {f.Value}"));
    }

    public sealed record LogFieldItem(string Key, string Value);
}
