using System.Collections.Generic;
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
                if (!string.IsNullOrWhiteSpace(ErrorMessage))
                {
                    return ErrorMessage;
                }

                var prefix = Index > 0 ? $"{Index}. " : "";
                return prefix + string.Join(", ", Fields.Select(item => $"{item.Key}: {item.Value}"));
            }
        }
    }

    public sealed record LogFieldItem(string Key, string Value);
}
