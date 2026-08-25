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
        public string Summary => ErrorMessage is null
        ? $"[{Index}] {string.Join(", ", Fields.Select(item => $"{item.Key}: {item.Value}"))}"
        : $"[{Index}] {string.Join(", ", Fields.Select(item => $"{item.Key}: {item.Value}"))}; Error: {ErrorMessage}";
    }

    public sealed record LogFieldItem(string Key, string Value);
}
