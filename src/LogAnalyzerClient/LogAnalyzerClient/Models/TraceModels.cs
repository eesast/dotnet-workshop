using System;
using System.Collections.Generic;

namespace LogAnalyzerClient.Models
{
    /// <summary>
    /// 瀑布图中的一条 span：某次调用链中的一次服务调用（T5.2）。
    /// 由一条 Call 日志得出：源服务（pod 名剥后缀）调用目标服务，从 <see cref="Start"/> 起耗时 <see cref="DurationMs"/> 毫秒。
    /// </summary>
    internal sealed record TraceSpan(
        string SourceService,
        string TargetService,
        DateTimeOffset Start,
        int DurationMs,
        bool IsError)
    {
        /// <summary>横条内展示的文本，如 「gateway -> authservice (18 ms)」。</summary>
        public string Label => $"{SourceService} -> {TargetService} ({DurationMs} ms)";
    }

    /// <summary>
    /// 一次请求（Request ID）的完整调用链，供瀑布图窗口可视化（T5.2）。
    /// <see cref="Spans"/> 已由 Agent 端按时间升序排列。
    /// </summary>
    internal sealed class TraceWaterfall
    {
        public string RequestId { get; set; } = "";
        public string FileName { get; set; } = "";
        public List<TraceSpan> Spans { get; } = new();
    }
}
