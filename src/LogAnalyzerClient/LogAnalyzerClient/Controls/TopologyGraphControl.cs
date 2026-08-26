using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LogAnalyzerClient.Models;
using LogAnalyzerClient.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace LogAnalyzerClient.Controls;

public sealed class TopologyGraphControl : Canvas
{
    public static readonly StyledProperty<IEnumerable<TopologyNodeItem>?> NodesProperty =
        AvaloniaProperty.Register<TopologyGraphControl, IEnumerable<TopologyNodeItem>?>(nameof(Nodes));

    public static readonly StyledProperty<IEnumerable<TopologyEdgeItem>?> EdgesProperty =
        AvaloniaProperty.Register<TopologyGraphControl, IEnumerable<TopologyEdgeItem>?>(nameof(Edges));

    public static readonly StyledProperty<ICommand?> EdgeSelectedCommandProperty =
        AvaloniaProperty.Register<TopologyGraphControl, ICommand?>(nameof(EdgeSelectedCommand));

    public IEnumerable<TopologyNodeItem>? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public IEnumerable<TopologyEdgeItem>? Edges
    {
        get => GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public ICommand? EdgeSelectedCommand
    {
        get => GetValue(EdgeSelectedCommandProperty);
        set => SetValue(EdgeSelectedCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NodesProperty ||
            change.Property == EdgesProperty ||
            change.Property == EdgeSelectedCommandProperty)
        {
            RebuildGraph();
        }
    }

    private void RebuildGraph()
    {
        Children.Clear();

        var nodes = Nodes?.ToArray() ?? [];
        var edges = Edges?.ToArray() ?? [];
        Width = Math.Max(500, nodes.Select(node => node.X).DefaultIfEmpty(0).Max() +
            TopologyLayout.NodeWidth + TopologyLayout.Margin);
        Height = Math.Max(260, nodes.Select(node => node.Y).DefaultIfEmpty(0).Max() +
            TopologyLayout.NodeHeight + TopologyLayout.Margin);

        if (nodes.Length == 0)
        {
            Children.Add(new TextBlock
            {
                Text = "No service calls were found in this file.",
                Opacity = 0.72,
            });
            SetLeft(Children[0], TopologyLayout.Margin);
            SetTop(Children[0], TopologyLayout.Margin);
            return;
        }

        var nodeByName = nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
        var badges = new List<Button>();
        foreach (var edge in edges)
        {
            if (!nodeByName.TryGetValue(edge.SourceService, out var source) ||
                !nodeByName.TryGetValue(edge.TargetService, out var target))
            {
                continue;
            }

            var (start, end, badgePosition, arrowDirectionPoint, geometry) =
                CreateEdgeGeometry(source, target);
            Children.Add(new ShapePath
            {
                Data = geometry,
                Stroke = Brushes.SlateGray,
                StrokeThickness = 2,
            });
            Children.Add(CreateArrow(end, arrowDirectionPoint));

            var badge = new Button
            {
                Content = edge.CallCount.ToString(),
                Command = EdgeSelectedCommand,
                CommandParameter = edge,
                FontSize = 11,
                MinWidth = 34,
                Padding = new Thickness(6, 2),
            };
            ToolTip.SetTip(badge, $"{edge.Summary}. Click to load matching logs.");
            SetLeft(badge, badgePosition.X - 17);
            SetTop(badge, badgePosition.Y - 14);
            badges.Add(badge);
        }

        foreach (var node in nodes)
        {
            var border = new Border
            {
                Width = TopologyLayout.NodeWidth,
                Height = TopologyLayout.NodeHeight,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.DodgerBlue,
                Background = Brushes.RoyalBlue,
                Child = new TextBlock
                {
                    Text = node.Name,
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(8),
                },
            };
            ToolTip.SetTip(border, node.Name);
            SetLeft(border, node.X);
            SetTop(border, node.Y);
            Children.Add(border);
        }

        foreach (var badge in badges)
        {
            Children.Add(badge);
        }
    }

    private static (
        Point Start,
        Point End,
        Point BadgePosition,
        Point ArrowDirectionPoint,
        StreamGeometry Geometry) CreateEdgeGeometry(
            TopologyNodeItem source,
            TopologyNodeItem target)
    {
        if (string.Equals(source.Name, target.Name, StringComparison.Ordinal))
        {
            var start = new Point(
                source.X + TopologyLayout.NodeWidth * 0.35,
                source.Y);
            var end = new Point(
                source.X + TopologyLayout.NodeWidth * 0.65,
                source.Y);
            var control1 = new Point(start.X - 10, source.Y - 35);
            var control2 = new Point(end.X + 10, source.Y - 35);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(start, false);
                context.CubicBezierTo(control1, control2, end, true);
                context.EndFigure(false);
            }

            return (
                start,
                end,
                new Point(source.X + TopologyLayout.NodeWidth / 2, source.Y - 28),
                control2,
                geometry);
        }

        var sourceCenter = new Point(
            source.X + TopologyLayout.NodeWidth / 2,
            source.Y + TopologyLayout.NodeHeight / 2);
        var targetCenter = new Point(
            target.X + TopologyLayout.NodeWidth / 2,
            target.Y + TopologyLayout.NodeHeight / 2);
        var deltaX = targetCenter.X - sourceCenter.X;
        var deltaY = targetCenter.Y - sourceCenter.Y;

        Point edgeStart;
        Point edgeEnd;
        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            edgeStart = new Point(
                deltaX >= 0 ? source.X + TopologyLayout.NodeWidth : source.X,
                sourceCenter.Y);
            edgeEnd = new Point(
                deltaX >= 0 ? target.X : target.X + TopologyLayout.NodeWidth,
                targetCenter.Y);
        }
        else
        {
            edgeStart = new Point(
                sourceCenter.X,
                deltaY >= 0 ? source.Y + TopologyLayout.NodeHeight : source.Y);
            edgeEnd = new Point(
                targetCenter.X,
                deltaY >= 0 ? target.Y : target.Y + TopologyLayout.NodeHeight);
        }

        var lineGeometry = new StreamGeometry();
        using (var context = lineGeometry.Open())
        {
            context.BeginFigure(edgeStart, false);
            context.LineTo(edgeEnd, true);
            context.EndFigure(false);
        }

        return (
            edgeStart,
            edgeEnd,
            new Point((edgeStart.X + edgeEnd.X) / 2, (edgeStart.Y + edgeEnd.Y) / 2),
            edgeStart,
            lineGeometry);
    }

    private static ShapePath CreateArrow(Point tip, Point directionPoint)
    {
        const double arrowLength = 10;
        const double arrowAngle = Math.PI / 7;
        var angle = Math.Atan2(tip.Y - directionPoint.Y, tip.X - directionPoint.X);
        var left = new Point(
            tip.X - arrowLength * Math.Cos(angle - arrowAngle),
            tip.Y - arrowLength * Math.Sin(angle - arrowAngle));
        var right = new Point(
            tip.X - arrowLength * Math.Cos(angle + arrowAngle),
            tip.Y - arrowLength * Math.Sin(angle + arrowAngle));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(tip, true);
            context.LineTo(left, true);
            context.LineTo(right, true);
            context.EndFigure(true);
        }

        return new ShapePath
        {
            Data = geometry,
            Fill = Brushes.SlateGray,
        };
    }
}
