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
        // 动态生成展示给用户的文本摘要[cite: 7]
        public string Summary 
        {
            get
            {
                var fieldsText = string.Join(", ", Fields.Select(f => $"{f.Key}: {f.Value}"));
                if (!string.IsNullOrWhiteSpace(ErrorMessage))
                {
                    return $"[{Index}] {fieldsText} | Error: {ErrorMessage}";
                }
                return $"[{Index}] {fieldsText}";
            }
        }
    }

    public sealed record LogFieldItem(string Key, string Value);
}