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
        // 当 ErrorMessage 非空时（如「尚未分析」「分析失败」），直接展示该提示信息；
        // 否则把日志条目的字段按 `序号 | Key: Value, Key: Value, ...` 的形式汇总成一行。
        public string Summary
        {
            get
            {
                if (ErrorMessage is not null)
                {
                    return ErrorMessage;
                }
                var fields = string.Join(", ", Fields.Select(f => $"{f.Key}: {f.Value}"));
                return $"{Index} | {fields}";
            }
        }
    }

    public sealed record LogFieldItem(string Key, string Value);
}
