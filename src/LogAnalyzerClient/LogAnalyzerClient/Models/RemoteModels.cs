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
                    return $"[{Index}] Error: {ErrorMessage}";
                }
                return $"[{Index}] {string.Join(", ", Fields.Select(f => $"{f.Key}={f.Value}"))}";
            }
        }
    }

    public sealed record LogFieldItem(string Key, string Value);

    /// <summary>
    /// T5.1(功能性: 查询排序) + T5.1(美观性: 表格显示) 的表格行模型。
    /// SeverityClass 供样式选择器着色（sev-info / sev-warning / sev-error）。
    /// </summary>
    public sealed record DisplayRow(
        int LineNo,
        string Timestamp,
        string PodName,
        string Severity,
        string EventType,
        string Detail,
        string SeverityClass)
    {
        public override string ToString() => $"[{LineNo}] {Severity} {Detail}";
    }
}
