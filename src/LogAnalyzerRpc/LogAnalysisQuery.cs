using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzerRpc.Protos;
using LogParser.Models;

namespace LogAnalyzerRpc
{
    /// <summary>
    /// 日志查询/排序的纯逻辑（不依赖 IO，便于单元测试）。
    /// 对应任务 T5.1.a.c（排序与查询）。
    /// </summary>
    public static class LogAnalysisQuery
    {
        /// <summary>
        /// 从 Pod 名称中提取服务名，例如 "gateway-0" -> "gateway"。
        /// 若名称末尾不是 "-数字" 的副本后缀，则原样返回。
        /// </summary>
        public static string GetServiceName(string podName)
        {
            if (string.IsNullOrEmpty(podName))
            {
                return podName;
            }

            int idx = podName.LastIndexOf('-');
            if (idx > 0 && idx < podName.Length - 1 && podName[(idx + 1)..].All(char.IsDigit))
            {
                return podName[..idx];
            }
            return podName;
        }

        /// <summary>
        /// 按 <see cref="LogFilter"/> 过滤，并按 <see cref="LogSortOptions"/> 排序。
        /// </summary>
        public static IReadOnlyList<LogEntry> FilterAndSort(
            IEnumerable<LogEntry> entries,
            LogFilter? filter,
            LogSortOptions? sort)
        {
            IEnumerable<LogEntry> result = entries;

            if (filter is not null)
            {
                result = result.Where(e => Matches(e, filter));
            }

            if (sort is not null && !string.IsNullOrWhiteSpace(sort.SortBy))
            {
                result = Sort(result, sort.SortBy, sort.IsDescending);
            }

            return result.ToList();
        }

        private static bool Matches(LogEntry entry, LogFilter filter)
        {
            // 1. 事件类型（Call / Request / Internal）
            if (filter.HasEventType && entry.EventType != ConvertEventType(filter.EventType))
            {
                return false;
            }

            // 2. 日志等级
            if (filter.HasSeverity && entry.Severity != ConvertSeverity(filter.Severity))
            {
                return false;
            }

            // 3. Pod 名称（精确匹配，忽略大小写）
            if (!string.IsNullOrWhiteSpace(filter.PodName)
                && !string.Equals(entry.PodName, filter.PodName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 4. 服务名（例如 gateway），匹配 Pod 所属服务
            if (!string.IsNullOrWhiteSpace(filter.ServiceName)
                && !string.Equals(GetServiceName(entry.PodName), filter.ServiceName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 5. 单个 Request ID
            if (!string.IsNullOrWhiteSpace(filter.RequestId)
                && !string.Equals(GetRequestId(entry), filter.RequestId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 6. 一组 Request ID（拓扑边点击时使用）
            if (filter.RequestIds is { Count: > 0 })
            {
                var ids = new HashSet<string>(filter.RequestIds, StringComparer.OrdinalIgnoreCase);
                var requestId = GetRequestId(entry);
                if (string.IsNullOrEmpty(requestId) || !ids.Contains(requestId))
                {
                    return false;
                }
            }

            // 7. 时间范围（含边界）
            if (filter.StartTime is not null && entry.Timestamp < filter.StartTime.ToDateTimeOffset())
            {
                return false;
            }
            if (filter.EndTime is not null && entry.Timestamp > filter.EndTime.ToDateTimeOffset())
            {
                return false;
            }

            return true;
        }

        private static string GetRequestId(LogEntry entry) => entry switch
        {
            CallLogEntry call => call.RequestId,
            RequestLogEntry request => request.RequestId,
            _ => string.Empty
        };

        private static IEnumerable<LogEntry> Sort(IEnumerable<LogEntry> entries, string sortBy, bool descending)
        {
            IOrderedEnumerable<LogEntry> ordered;
            switch (sortBy.Trim().ToLowerInvariant())
            {
                case "lineno":
                    ordered = Order(entries, e => e.LineNo, descending);
                    break;
                case "timestamp":
                    ordered = Order(entries, e => e.Timestamp, descending);
                    break;
                case "severity":
                    ordered = Order(entries, e => e.Severity, descending);
                    break;
                case "podname":
                    ordered = Order(entries, e => e.PodName, descending, StringComparer.OrdinalIgnoreCase);
                    break;
                default:
                    // 未知字段回退到行号排序
                    ordered = Order(entries, e => e.LineNo, descending);
                    break;
            }

            // 以 LineNo 作为稳定次排序，保证结果可预期
            return ordered.ThenBy(e => e.LineNo);
        }

        private static IOrderedEnumerable<LogEntry> Order<TKey>(
            IEnumerable<LogEntry> entries,
            Func<LogEntry, TKey> keySelector,
            bool descending,
            IComparer<TKey>? comparer = null)
        {
            return descending
                ? entries.OrderByDescending(keySelector, comparer)
                : entries.OrderBy(keySelector, comparer);
        }

        private static LogEventType ConvertEventType(LogEventTypeEnum value) => value switch
        {
            LogEventTypeEnum.Call => LogEventType.Call,
            LogEventTypeEnum.Request => LogEventType.Request,
            LogEventTypeEnum.Internal => LogEventType.Internal,
            _ => LogEventType.Call
        };

        private static LogSeverity ConvertSeverity(LogSeverityEnum value) => value switch
        {
            LogSeverityEnum.Info => LogSeverity.Info,
            LogSeverityEnum.Warning => LogSeverity.Warning,
            LogSeverityEnum.Error => LogSeverity.Error,
            _ => LogSeverity.Info
        };
    }

    /// <summary>云服务拓扑推断结果。</summary>
    public sealed record TopologyResult(
        IReadOnlyList<string> Nodes,
        IReadOnlyList<TopologyEdgeInfo> Edges);

    /// <summary>一条拓扑边的聚合信息。</summary>
    public sealed record TopologyEdgeInfo(
        string SourceService,
        string TargetService,
        int CallCount,
        IReadOnlyList<string> RequestIds);

    /// <summary>云服务拓扑推断逻辑（T5.1.a.d）。</summary>
    public static class TopologyBuilder
    {
        /// <summary>
        /// 从日志条目中推断调用拓扑。仅统计 Call 类型的日志：
        /// 单个服务视为结点，Call 日志的 Pod 所属服务 -> target-service 视为有向边。
        /// </summary>
        public static TopologyResult Build(IEnumerable<LogEntry> entries)
        {
            var nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var edges = new Dictionary<(string Source, string Target), List<string>>();

            foreach (var entry in entries)
            {
                if (entry is not CallLogEntry call)
                {
                    continue;
                }

                string source = LogAnalysisQuery.GetServiceName(call.PodName);
                string target = call.TargetService;

                nodes.Add(source);
                nodes.Add(target);

                var key = (source, target);
                if (!edges.TryGetValue(key, out var requestIds))
                {
                    requestIds = new List<string>();
                    edges[key] = requestIds;
                }
                if (!string.IsNullOrEmpty(call.RequestId))
                {
                    requestIds.Add(call.RequestId);
                }
            }

            var nodeList = nodes.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            var edgeList = edges
                .Select(kv => new TopologyEdgeInfo(
                    SourceService: kv.Key.Source,
                    TargetService: kv.Key.Target,
                    CallCount: kv.Value.Count,
                    RequestIds: kv.Value))
                .OrderBy(e => e.SourceService, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.TargetService, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new TopologyResult(nodeList, edgeList);
        }
    }
}
