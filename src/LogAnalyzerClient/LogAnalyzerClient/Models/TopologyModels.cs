using System.Collections.Generic;

namespace LogAnalyzerClient.Models
{
    /// <summary>
    /// 拓扑图中的一条有向边：source_service 调用了 target_service，共 CallCount 条 Call 日志。
    /// </summary>
    internal sealed record TopologyEdge(string SourceService, string TargetService, int CallCount)
    {
        /// <summary>
        /// 用于下拉 / 列表展示的文本，如 「gateway → userservice (12 calls)」。
        /// </summary>
        public string Display => $"{SourceService} -> {TargetService} ({CallCount} call{(CallCount == 1 ? "" : "s")})";
    }

    /// <summary>
    /// 某个日志文件推断出的云服务调用拓扑：结点集合 + 有向边集合。
    /// </summary>
    internal sealed class TopologyGraph
    {
        public string FileName { get; set; } = "";
        public List<string> Nodes { get; } = new();
        public List<TopologyEdge> Edges { get; } = new();
    }
}
