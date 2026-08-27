using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Grpc.Core;
using LogAnalyzerClient.Helpers;
using LogAnalyzerClient.Services;
using LogAnalyzerRpc;
using LogAnalyzerRpc.Protos;
using LogParser.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LogAnalyzerClient.Views
{
    using LogAnalyzerAgentServiceClient = LogAnalyzerAgentService.LogAnalyzerAgentServiceClient;

    public partial class TopologyWindow : Window
    {
        private readonly LogAnalyzerAgentServiceClient _client;
        private readonly string _fileName;

        private static readonly IBrush NodeBrush = new SolidColorBrush(Color.Parse("#1E293B"));
        private static readonly IBrush NodeTextBrush = Brushes.White;
        private static readonly IBrush EdgeBrush = new SolidColorBrush(Color.Parse("#64748B"));
        private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#0F172A"));

        public TopologyWindow() : this("http://localhost:5000", "")
        {
        }

        public TopologyWindow(string serverUrl, string fileName)
        {
            InitializeComponent();
            _client = LogAgentClientManager.CreateClient(serverUrl);
            _fileName = fileName;
            Title = $"Service Topology - {fileName}";
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Loading topology...";
                var response = await _client.GetTopologyAsync(new GetTopologyRequest { FileName = _fileName });
                if (!response.Status.Success)
                {
                    StatusText.Text = $"Failed: {response.Status.Code} - {response.Status.Message}";
                    return;
                }

                Render(response);
                StatusText.Text =
                    $"{response.Nodes.Count} services, {response.Edges.Count} call edges. " +
                    "点击图中的边可查看这条边对应的 Call 日志。";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void Render(GetTopologyResponse response)
        {
            TopologyCanvas.Children.Clear();
            EdgeLogListBox.Items.Clear();

            var nodeNames = response.Nodes.Select(n => n.ServiceName).ToList();
            var edges = response.Edges
                .Select(e => (e.SourceService, e.TargetService, e.CallCount, (IReadOnlyList<string>)e.RequestIds.ToList()))
                .ToList();

            var layout = TopologyLayout.Compute(nodeNames, edges);
            TopologyCanvas.Width = layout.CanvasWidth;
            TopologyCanvas.Height = layout.CanvasHeight;

            // 先画边（在结点下层），并标注调用次数；方向为左 -> 右
            foreach (var edge in layout.Edges)
            {
                var line = new Line
                {
                    StartPoint = new Point(edge.X1, edge.Y1),
                    EndPoint = new Point(edge.X2, edge.Y2),
                    Stroke = EdgeBrush,
                    StrokeThickness = 2,
                    Tag = edge,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                line.Tapped += OnEdgeTapped;
                TopologyCanvas.Children.Add(line);

                AddArrowHead(edge.X1, edge.Y1, edge.X2, edge.Y2);
                AddEdgeLabel(edge);
            }

            // 再画结点
            foreach (var node in layout.Nodes)
            {
                var border = new Border
                {
                    Width = TopologyLayout.NodeWidth,
                    Height = TopologyLayout.NodeHeight,
                    Background = NodeBrush,
                    CornerRadius = new CornerRadius(8),
                    Child = new TextBlock
                    {
                        Text = node.Service,
                        Foreground = NodeTextBrush,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        FontWeight = FontWeight.SemiBold,
                    },
                };
                Canvas.SetLeft(border, node.X);
                Canvas.SetTop(border, node.Y);
                TopologyCanvas.Children.Add(border);
            }
        }

        private void AddArrowHead(double x1, double y1, double x2, double y2)
        {
            double angle = Math.Atan2(y2 - y1, x2 - x1);
            const double length = 10;
            double a1 = angle - Math.PI / 6;
            double a2 = angle + Math.PI / 6;

            var arrow = new Polygon
            {
                Fill = EdgeBrush,
                Points = new Points
                {
                    new Point(x2, y2),
                    new Point(x2 - length * Math.Cos(a1), y2 - length * Math.Sin(a1)),
                    new Point(x2 - length * Math.Cos(a2), y2 - length * Math.Sin(a2)),
                },
            };
            TopologyCanvas.Children.Add(arrow);
        }

        private void AddEdgeLabel(TopologyLayout.EdgePosition edge)
        {
            var label = new TextBlock
            {
                Text = edge.CallCount.ToString(),
                Foreground = LabelBrush,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Background = Brushes.White,
                Padding = new Thickness(4, 0),
            };
            Canvas.SetLeft(label, (edge.X1 + edge.X2) / 2 - 6);
            Canvas.SetTop(label, (edge.Y1 + edge.Y2) / 2 - 8);
            TopologyCanvas.Children.Add(label);
        }

        private async void OnEdgeTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Line { Tag: TopologyLayout.EdgePosition edge })
            {
                await ShowEdgeLogs(edge);
            }
        }

        private async Task ShowEdgeLogs(TopologyLayout.EdgePosition edge)
        {
            EdgeLogListBox.Items.Clear();
            EdgeLogListBox.Items.Add($">>> {edge.Source} -> {edge.Target}  ({edge.CallCount} calls)");

            try
            {
                var filter = new LogFilter();
                filter.RequestIds.AddRange(edge.RequestIds);
                var request = new QueryAnalysisResultRequest { FileName = _fileName, Filter = filter };

                using var call = _client.QueryAnalysisResult(request);
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Status.Success)
                    {
                        EdgeLogListBox.Items.Add($"[error] {response.Status.Message}");
                        continue;
                    }
                    if (response.PayloadCase == GetAnalysisResultResponse.PayloadOneofCase.LogEntry)
                    {
                        var entry = GrpcTypeConverter.ConvertFromGrpc(response.LogEntry);
                        if (entry is not null)
                        {
                            EdgeLogListBox.Items.Add(FormatEntry(entry));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EdgeLogListBox.Items.Add($"[error] {ex.Message}");
            }
        }

        private static string FormatEntry(LogEntry entry)
        {
            return entry switch
            {
                CallLogEntry call => $"#{call.LineNo}  CALL  {call.PodName} -> {call.TargetService}  ({call.DurationMs} ms)  req={call.RequestId}",
                RequestLogEntry req => $"#{req.LineNo}  REQUEST  {req.PodName}  {req.Method} {req.Path}  -> {req.StatusCode}  req={req.RequestId}",
                InternalLogEntry ie => $"#{ie.LineNo}  INTERNAL  {ie.PodName}  {ie.ExceptionName}: {ie.ExceptionMessage}",
                _ => entry.ToString() ?? string.Empty,
            };
        }
    }
}
