using System;
using System.Collections.Generic;
using System.Linq;

namespace LogAnalyzerClient.Helpers
{
    /// <summary>
    /// 云服务拓扑图的简单分层（layered）布局算法。
    /// 从入度为 0 的根服务出发做 BFS 分层，同层节点纵向排列，
    /// 调用关系方向为「左 -> 右」，避免结点重叠。
    /// </summary>
    public static class TopologyLayout
    {
        public const double NodeWidth = 150;
        public const double NodeHeight = 40;
        public const double HSpan = 240;
        public const double VSpan = 72;
        public const double Margin = 40;

        public sealed record NodePosition(string Service, double X, double Y)
        {
            public double CenterX => X + NodeWidth / 2;
            public double CenterY => Y + NodeHeight / 2;
        }

        public sealed record EdgePosition(
            string Source,
            string Target,
            int CallCount,
            IReadOnlyList<string> RequestIds,
            double X1, double Y1, double X2, double Y2);

        public sealed record LayoutResult(
            IReadOnlyList<NodePosition> Nodes,
            IReadOnlyList<EdgePosition> Edges,
            double CanvasWidth,
            double CanvasHeight);

        public static LayoutResult Compute(
            IReadOnlyList<string> nodeNames,
            IReadOnlyList<(string Source, string Target, int Count, IReadOnlyList<string> RequestIds)> edges)
        {
            var nodeSet = new HashSet<string>(nodeNames, StringComparer.OrdinalIgnoreCase);

            var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodeSet)
            {
                adjacency[node] = new List<string>();
                inDegree[node] = 0;
            }

            foreach (var edge in edges)
            {
                if (!adjacency.ContainsKey(edge.Source)) adjacency[edge.Source] = new List<string>();
                if (!inDegree.ContainsKey(edge.Source)) inDegree[edge.Source] = 0;
                if (!adjacency.ContainsKey(edge.Target)) adjacency[edge.Target] = new List<string>();
                if (!inDegree.ContainsKey(edge.Target)) inDegree[edge.Target] = 0;

                adjacency[edge.Source].Add(edge.Target);
                inDegree[edge.Target] = inDegree[edge.Target] + 1;
            }

            // 分层：BFS，从入度为 0 的根开始
            var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            var roots = nodeSet.Where(n => inDegree[n] == 0).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (roots.Count == 0)
            {
                roots = nodeSet.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }

            foreach (var root in roots)
            {
                depth[root] = 0;
                queue.Enqueue(root);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in adjacency[current])
                {
                    if (!depth.ContainsKey(next))
                    {
                        depth[next] = depth[current] + 1;
                        queue.Enqueue(next);
                    }
                }
            }

            // 未访问到的孤立结点放到第 0 层
            foreach (var node in nodeSet)
            {
                depth.TryAdd(node, 0);
            }

            var layers = depth
                .GroupBy(kv => kv.Value)
                .OrderBy(g => g.Key)
                .Select(g => g.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key).ToList())
                .ToList();

            int maxLayerHeight = layers.Max(l => l.Count);
            int layerCount = layers.Count;

            double canvasWidth = Margin * 2 + Math.Max(0, layerCount - 1) * HSpan + NodeWidth;
            double canvasHeight = Margin * 2 + (maxLayerHeight - 1) * VSpan + NodeHeight;

            var positions = new Dictionary<string, NodePosition>(StringComparer.OrdinalIgnoreCase);
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                double x = Margin + layerIndex * HSpan;
                double offsetY = (maxLayerHeight - layer.Count) * VSpan / 2.0;
                for (int i = 0; i < layer.Count; i++)
                {
                    double y = Margin + i * VSpan + offsetY;
                    var pos = new NodePosition(layer[i], x, y);
                    positions[layer[i]] = pos;
                }
            }

            var edgePositions = new List<EdgePosition>();
            foreach (var edge in edges)
            {
                if (!positions.TryGetValue(edge.Source, out var src) || !positions.TryGetValue(edge.Target, out var dst))
                {
                    continue;
                }
                double x1 = src.CenterX + NodeWidth / 2;
                double y1 = src.CenterY;
                double x2 = dst.CenterX - NodeWidth / 2;
                double y2 = dst.CenterY;
                edgePositions.Add(new EdgePosition(edge.Source, edge.Target, edge.Count, edge.RequestIds, x1, y1, x2, y2));
            }

            return new LayoutResult(positions.Values.OrderBy(p => p.X).ThenBy(p => p.Y).ToList(), edgePositions, canvasWidth, canvasHeight);
        }
    }
}
