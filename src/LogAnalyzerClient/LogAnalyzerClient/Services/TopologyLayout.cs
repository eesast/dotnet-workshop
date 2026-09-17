using LogAnalyzerClient.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LogAnalyzerClient.Services;

public static class TopologyLayout
{
    public const double NodeWidth = 150;
    public const double NodeHeight = 56;
    public const double HorizontalSpacing = 220;
    public const double VerticalSpacing = 100;
    public const double Margin = 40;

    public static IReadOnlyList<TopologyNodeItem> Arrange(
        IEnumerable<string> nodeNames,
        IEnumerable<TopologyEdgeItem> edges)
    {
        ArgumentNullException.ThrowIfNull(nodeNames);
        ArgumentNullException.ThrowIfNull(edges);

        var edgeList = edges.ToArray();
        var allNames = nodeNames
            .Concat(edgeList.SelectMany(edge => new[]
            {
                edge.SourceService,
                edge.TargetService,
            }))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var remaining = new HashSet<string>(allNames, StringComparer.Ordinal);
        var layers = new List<IReadOnlyList<string>>();

        while (remaining.Count > 0)
        {
            var currentLayer = remaining
                .Where(target => !edgeList.Any(edge =>
                    edge.SourceService != edge.TargetService &&
                    remaining.Contains(edge.SourceService) &&
                    string.Equals(edge.TargetService, target, StringComparison.Ordinal)))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            // Break a cycle deterministically so every graph can still be displayed.
            if (currentLayer.Length == 0)
            {
                currentLayer = [remaining.OrderBy(name => name, StringComparer.Ordinal).First()];
            }

            layers.Add(currentLayer);
            remaining.ExceptWith(currentLayer);
        }

        return layers
            .SelectMany((layer, layerIndex) => layer.Select((name, itemIndex) =>
                new TopologyNodeItem(
                    name,
                    Margin + layerIndex * HorizontalSpacing,
                    Margin + itemIndex * VerticalSpacing)))
            .ToArray();
    }
}
