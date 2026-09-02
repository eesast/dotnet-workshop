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
                var fields = string.Join(", ", Fields.Select(f => $"{f.Key}={f.Value}"));
                if (ErrorMessage is not null)
                {
                    return $"[{Index}] {(fields.Length > 0 ? fields + " | " : "")}{ErrorMessage}";
                }

                return $"[{Index}] {fields}";
            }
        }
    }

    public sealed record LogFieldItem(string Key, string Value);
}
