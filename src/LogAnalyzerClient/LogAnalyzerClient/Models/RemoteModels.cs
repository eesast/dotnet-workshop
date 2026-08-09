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
                if (ErrorMessage is not null)
                {
                    return ErrorMessage;
                }

                var fields = string.Join(", ",
                    Fields.Select(item => $"{item.Key}: {item.Value}"));
                return Index < 0 ? fields : $"{Index} | {fields}";
            }
        }
    }

    public sealed record LogFieldItem(string Key, string Value);
}
