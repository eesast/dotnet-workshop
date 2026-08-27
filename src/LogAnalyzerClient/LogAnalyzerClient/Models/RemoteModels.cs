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
        public string Summary => ErrorMessage ?? string.Join(" ", Fields?.Select(f => $"{f.Key}: {f.Value}") ?? []);
    }

    public sealed record LogFieldItem(string Key, string Value);
}
