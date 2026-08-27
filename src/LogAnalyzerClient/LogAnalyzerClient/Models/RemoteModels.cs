using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LogAnalyzerClient.Models
{
    public enum LogTypeFilter
    {
        All,
        Call,
        Request,
        Internal,
    }

    public enum CallCountSort
    {
        None,
        Ascending,
        Descending,
    }

    public sealed record LogFileItem(string FileName)
    {
        public override string ToString() => FileName;
    }

    public sealed record LogFields(int Index, IReadOnlyList<LogFieldItem> Fields, string? ErrorMessage)
    {
        public string Summary => ErrorMessage ??
            $"{Index} | {string.Join(", ", Fields.Select(item => $"{item.Key}: {item.Value}"))}";
    }

    public sealed record LogFieldItem(string Key, string Value);
}
