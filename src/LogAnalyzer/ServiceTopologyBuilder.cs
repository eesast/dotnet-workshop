using LogParser.Models;

namespace LogAnalyzer;

public static class ServiceTopologyBuilder
{
    public static ServiceTopology Build(IEnumerable<LogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var edges = entries
            .OfType<CallLogEntry>()
            .Select(entry => new
            {
                Entry = entry,
                SourceService = GetSourceService(entry.PodName),
                TargetService = entry.TargetService,
            })
            .GroupBy(item => (item.SourceService, item.TargetService))
            .OrderBy(group => group.Key.SourceService, StringComparer.Ordinal)
            .ThenBy(group => group.Key.TargetService, StringComparer.Ordinal)
            .Select(group => new ServiceEdge(
                group.Key.SourceService,
                group.Key.TargetService,
                group.Select(item => item.Entry).ToArray()))
            .ToArray();

        var nodes = edges
            .SelectMany(edge => new[] { edge.SourceService, edge.TargetService })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new ServiceNode(name))
            .ToArray();

        return new ServiceTopology(nodes, edges);
    }

    public static string GetSourceService(string podName)
    {
        ArgumentNullException.ThrowIfNull(podName);

        var separatorIndex = podName.LastIndexOf('-');
        if (separatorIndex <= 0 || separatorIndex == podName.Length - 1)
        {
            return podName;
        }

        var suffix = podName.AsSpan(separatorIndex + 1);
        return suffix.IndexOfAnyExceptInRange('0', '9') < 0
            ? podName[..separatorIndex]
            : podName;
    }
}
