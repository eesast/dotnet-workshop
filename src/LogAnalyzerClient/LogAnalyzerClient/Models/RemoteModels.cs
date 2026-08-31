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
                // 如果是错误消息（非空），显示错误
                if (!string.IsNullOrEmpty(ErrorMessage))
                {
                    return $"❌ {ErrorMessage}";
                }

                // 如果是 Header（Index == -1），显示文件状态
                if (Index == -1)
                {
                    if (Fields.Count == 0)
                    {
                        return "📋 文件信息";
                    }
                    return "📋 文件信息 (无日志条目)";
                }

                // 正常日志条目：显示关键信息
                var dict = Fields.ToDictionary(f => f.Key, f => f.Value);

                // 尝试提取关键字段
                string lineNo = dict.TryGetValue("LineNo", out var ln) ? ln : "?";
                string eventType = dict.TryGetValue("EventType", out var et) ? et : "?";
                string severity = dict.TryGetValue("Severity", out var sev) ? sev : "?";

                // 根据不同类型显示不同摘要
                if (eventType == "Call")
                {
                    string target = dict.TryGetValue("TargetService", out var ts) ? ts : "?";
                    string duration = dict.TryGetValue("DurationMs", out var dm) ? dm : "?";
                    return $"#{lineNo} [Call] → {target} ({duration}ms)";
                }
                else if (eventType == "Request")
                {
                    string method = dict.TryGetValue("Method", out var m) ? m : "?";
                    string path = dict.TryGetValue("Path", out var p) ? p : "?";
                    string status = dict.TryGetValue("StatusCode", out var sc) ? sc : "?";
                    return $"#{lineNo} [Request] {method} {path} → {status}";
                }
                else if (eventType == "Internal")
                {
                    string exName = dict.TryGetValue("ExceptionName", out var en) ? en : "?";
                    return $"#{lineNo} [Internal] ⚠ {exName}";
                }
                else
                {
                    return $"#{lineNo} [{eventType}] {severity}";
                }
            }
        }

    }

    public sealed record LogFieldItem(string Key, string Value);
}
