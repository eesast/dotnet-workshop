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
        public string Summary
        {
            get
            {
                var prefix = Index >= 0 ? $"[{Index}] " : "";
                var fields = string.Join(" | ", Fields.Select(item => $"{item.Key}: {item.Value}"));
                if (string.IsNullOrEmpty(ErrorMessage))
                {
                    return prefix + fields;
                }
                return $"{prefix}{fields} | Error: {ErrorMessage}";
            }
        }
    }

    public sealed record LogFieldItem(string Key, string Value);
}
