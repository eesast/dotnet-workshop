using System;
using System.Globalization;
using LogAnalyzerClient.Models;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerClient.Helpers
{
    /// <summary>
    /// 解析浏览器端 prompt 输入的紧凑查询语法，用于在不支持自定义窗口的浏览器环境下构造 <see cref="QueryFilter"/>。
    /// 语法（各项以空格分隔，均可选）：
    ///   type=Call,Request       事件类型，逗号分隔
    ///   severity=Warning,Error  日志等级，逗号分隔
    ///   service=gateway         服务（pod 名）子串
    ///   request=&lt;id 子串&gt;     Request ID 子串
    ///   from=2026-06-05         时间下界
    ///   to=2026-06-05T17:00:00Z 时间上界
    /// 无法识别的键或枚举名会被忽略，以保持容错。
    /// </summary>
    internal static class QueryFilterParser
    {
        public static QueryFilter Parse(string text)
        {
            var filter = new QueryFilter();
            foreach (var token in text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eqIndex = token.IndexOf('=');
                if (eqIndex <= 0 || eqIndex == token.Length - 1)
                {
                    continue;
                }
                string key = token[..eqIndex].ToLowerInvariant();
                string value = token[(eqIndex + 1)..];

                switch (key)
                {
                    case "type":
                        AddEventTypes(filter, value);
                        break;
                    case "severity":
                        AddSeverities(filter, value);
                        break;
                    case "service":
                        filter.ServicePattern = value;
                        break;
                    case "request":
                        filter.RequestIdPattern = value;
                        break;
                    case "from":
                        filter.StartTime = TryParseTime(value);
                        break;
                    case "to":
                        filter.EndTime = TryParseTime(value);
                        break;
                    // 其余未知键忽略
                }
            }
            return filter;
        }

        private static void AddEventTypes(QueryFilter filter, string value)
        {
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse<LogEventTypeEnum>(part.Trim(), ignoreCase: true, out var t))
                {
                    filter.EventTypes.Add(t);
                }
            }
        }

        private static void AddSeverities(QueryFilter filter, string value)
        {
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse<LogSeverityEnum>(part.Trim(), ignoreCase: true, out var s))
                {
                    filter.Severities.Add(s);
                }
            }
        }

        private static DateTimeOffset? TryParseTime(string value)
        {
            if (DateTimeOffset.TryParse(
                    value.Trim(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dto))
            {
                return dto;
            }
            return null;
        }
    }
}
