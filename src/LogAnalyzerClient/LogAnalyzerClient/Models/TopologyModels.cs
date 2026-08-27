namespace LogAnalyzerClient.Models;

public sealed record TopologyNodeItem(string Name, double X, double Y);

public sealed record TopologyEdgeItem(
    string SourceService,
    string TargetService,
    int CallCount)
{
    public string Summary => $"{SourceService} → {TargetService} ({CallCount} calls)";
}
