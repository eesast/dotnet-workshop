using System;
using System.Collections.Generic;
using Google.Protobuf.WellKnownTypes;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerClient.Models
{
    /// <summary>
    /// 用户在查询对话框中选择的过滤条件。任一维度为空集合 / null / 空白字符串时，
    /// 表示「不过滤该维度」。可转换为对应的 gRPC 请求。
    /// </summary>
    internal sealed class QueryFilter
    {
        // 限定日志事件类型（Call / Request / Internal）。为空表示不限定。
        public HashSet<LogEventTypeEnum> EventTypes { get; } = new();

        // 限定日志等级（Info / Warning / Error）。为空表示不限定。
        public HashSet<LogSeverityEnum> Severities { get; } = new();

        // 按 Request ID 子串匹配（大小写不敏感）。Internal 日志无 Request ID。
        public string RequestIdPattern { get; set; } = "";

        // 按产生日志的服务（pod 名）子串匹配（大小写不敏感）。
        public string ServicePattern { get; set; } = "";

        // 时间范围下界（含）。null 表示不限定下界。
        public DateTimeOffset? StartTime { get; set; }

        // 时间范围上界（含）。null 表示不限定上界。
        public DateTimeOffset? EndTime { get; set; }

        /// <summary>
        /// 是否所有维度均未填写（即等价于「查询全部」）。
        /// </summary>
        public bool IsEmpty =>
            EventTypes.Count == 0 &&
            Severities.Count == 0 &&
            string.IsNullOrWhiteSpace(RequestIdPattern) &&
            string.IsNullOrWhiteSpace(ServicePattern) &&
            StartTime is null &&
            EndTime is null;

        /// <summary>
        /// 构造发送给 Agent 的 gRPC 查询请求。
        /// </summary>
        public QueryAnalysisResultRequest ToRequest(string fileName)
        {
            var request = new QueryAnalysisResultRequest
            {
                FileName = fileName,
            };
            request.EventTypes.AddRange(EventTypes);
            request.Severities.AddRange(Severities);
            request.RequestIdPattern = RequestIdPattern?.Trim() ?? "";
            request.ServicePattern = ServicePattern?.Trim() ?? "";
            if (StartTime is not null)
            {
                request.StartTime = Timestamp.FromDateTimeOffset(StartTime.Value);
            }
            if (EndTime is not null)
            {
                request.EndTime = Timestamp.FromDateTimeOffset(EndTime.Value);
            }
            return request;
        }
    }
}
