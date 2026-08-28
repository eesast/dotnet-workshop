using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LogAnalyzerClient.Models;

namespace LogAnalyzerClient;

public partial class TopologyWindow : Window
{
    // 固定画布尺寸（与 axaml 中的 Canvas 一致）。
    private const double CanvasWidth = 720;
    private const double CanvasHeight = 460;
    private const double CenterX = CanvasWidth / 2;
    private const double CenterY = CanvasHeight / 2;
    private const double LayoutRadius = 168;

    // 结点（服务名）胶囊尺寸。
    private const double NodeWidth = 100;
    private const double NodeHeight = 42;
    // 连线端点从结点中心回退的距离，使箭头落在结点边缘外侧。
    private const double NodePullback = 36;
    private const double ArrowSize = 11;

    private static readonly ISolidColorBrush NodeFill = new SolidColorBrush(Color.Parse("#2563eb"));
    private static readonly ISolidColorBrush NodeBorder = new SolidColorBrush(Color.Parse("#1d4ed8"));
    private static readonly ISolidColorBrush EdgeBrush = new SolidColorBrush(Color.Parse("#6b7280"));
    // 近乎透明但仍参与命中测试的画刷，用于加粗的边点击热区。
    private static readonly ISolidColorBrush HitBrush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

    private readonly TopologyGraph _graph;

    public TopologyWindow()
    {
        InitializeComponent();
    }

    // 构造函数接收 internal 模型，故也声明为 internal，避免可访问性不一致。
    internal TopologyWindow(TopologyGraph graph) : this()
    {
        _graph = graph;
        HeaderTextBlock.Text = $"Call topology of '{graph.FileName}'  -  " +
            $"{graph.Nodes.Count} service{(graph.Nodes.Count == 1 ? "" : "s")}, " +
            $"{graph.Edges.Count} edge{(graph.Edges.Count == 1 ? "" : "s")}";
        Render();
    }

    private void Render()
    {
        var canvas = GraphCanvas;
        canvas.Children.Clear();
        if (_graph.Nodes.Count == 0)
        {
            return;
        }

        // 1) 圆形布局：把所有结点均匀分布在一个圆周上（从正上方开始顺时针）。
        var positions = new Dictionary<string, Point>();
        int n = _graph.Nodes.Count;
        for (int i = 0; i < n; i++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * i / n;
            double radius = n == 1 ? 0 : LayoutRadius;
            double x = CenterX + radius * Math.Cos(angle);
            double y = CenterY + radius * Math.Sin(angle);
            positions[_graph.Nodes[i]] = new Point(x, y);
        }

        // 2) 先绘制所有边（线 + 箭头 + 透明热区），再绘制结点，使结点覆盖边的端点。
        foreach (var edge in _graph.Edges)
        {
            if (!positions.TryGetValue(edge.SourceService, out var s) ||
                !positions.TryGetValue(edge.TargetService, out var t))
            {
                continue;
            }
            DrawEdge(canvas, edge, s, t);
        }

        // 3) 绘制结点。
        foreach (var node in _graph.Nodes)
        {
            DrawNode(canvas, node, positions[node]);
        }
    }

    private void DrawNode(Canvas canvas, string service, Point center)
    {
        var label = new TextBlock
        {
            Text = service,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var border = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            CornerRadius = new CornerRadius(NodeHeight / 2),
            Background = NodeFill,
            BorderBrush = NodeBorder,
            BorderThickness = new Thickness(1.5),
            Child = label,
        };
        ToolTip.SetTip(border, service);
        Canvas.SetLeft(border, center.X - NodeWidth / 2);
        Canvas.SetTop(border, center.Y - NodeHeight / 2);
        canvas.Children.Add(border);
    }

    private void DrawEdge(Canvas canvas, TopologyEdge edge, Point s, Point t)
    {
        // 自环：在结点上方画一个小圆环作为可点击的边。
        if (edge.SourceService == edge.TargetService)
        {
            var loop = new Ellipse
            {
                Width = 28,
                Height = 28,
                Stroke = EdgeBrush,
                StrokeThickness = 2,
                Fill = HitBrush,
            };
            Canvas.SetLeft(loop, s.X - 14);
            Canvas.SetTop(loop, s.Y - NodeHeight / 2 - 30);
            loop.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                Close(edge);
            };
            canvas.Children.Add(loop);
            return;
        }

        double dx = t.X - s.X;
        double dy = t.Y - s.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6)
        {
            return;
        }
        double ux = dx / len;
        double uy = dy / len;

        // 连线两端各回退 NodePullback，使其落在结点边缘，并定义箭头尖端位置。
        Point start = new(s.X + ux * NodePullback, s.Y + uy * NodePullback);
        Point tip = new(t.X - ux * NodePullback, t.Y - uy * NodePullback);

        var line = new Line
        {
            StartPoint = start,
            EndPoint = tip,
            Stroke = EdgeBrush,
            StrokeThickness = 2,
        };
        canvas.Children.Add(line);

        // 箭头三角形。
        double ang = Math.Atan2(uy, ux);
        double a1 = ang + 150 * Math.PI / 180.0;
        double a2 = ang - 150 * Math.PI / 180.0;
        Point b1 = new(tip.X + ArrowSize * Math.Cos(a1), tip.Y + ArrowSize * Math.Sin(a1));
        Point b2 = new(tip.X + ArrowSize * Math.Cos(a2), tip.Y + ArrowSize * Math.Sin(a2));
        var arrow = new Polygon
        {
            Points = new Point[] { tip, b1, b2 },
            Fill = EdgeBrush,
        };
        canvas.Children.Add(arrow);

        // 透明的加粗热区，让细线也容易点击。
        var hit = new Line
        {
            StartPoint = s,
            EndPoint = t,
            Stroke = HitBrush,
            StrokeThickness = 16,
        };
        hit.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Close(edge);
        };
        canvas.Children.Add(hit);
    }
}
