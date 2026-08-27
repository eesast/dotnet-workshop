using LogParser.Models;

namespace LogAnalyzer;

public sealed record ServiceTopology(
    IReadOnlyList<ServiceNode> Nodes,
    IReadOnlyList<ServiceEdge> Edges);

public sealed record ServiceNode(string Name);

public sealed record ServiceEdge(
    string SourceService,
    string TargetService,
    IReadOnlyList<CallLogEntry> Calls)
{
    public int CallCount => Calls.Count;
}
