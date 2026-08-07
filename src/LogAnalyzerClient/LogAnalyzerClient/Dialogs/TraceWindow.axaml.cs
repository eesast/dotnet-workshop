using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LogAnalyzerClient.Models;

namespace LogAnalyzerClient;

/// <summary>
/// 链路追踪瀑布图窗口（T5.2）。把一次请求（Request ID）的各次服务调用按时间从左到右
/// 画成横向条形：条的左偏移对应该调用的起始时刻（相对请求起点），宽度对应耗时，
/// 出错的调用（原 Call 日志 Severity == Error）用红色标出。
/// </summary>
public partial class TraceWindow : Window
{
    private const double CanvasWidth = 780;
    private const double CanvasHeightBase = 440;
    private const double LeftMargin = 14;
    private const double RightMargin = 14;
    private const double TopMargin = 30;
    private const double RowHeight = 46;
    private const double RowGap = 12;
    private const double MinBarWidth = 28;

    private static readonly ISolidColorBrush OkFill = new SolidColorBrush(Color.Parse("#2563eb"));
    private static readonly ISolidColorBrush OkBorder = new SolidColorBrush(Color.Parse("#1d4ed8"));
    private static readonly ISolidColorBrush ErrFill = new SolidColorBrush(Color.Parse("#c53030"));
    private static readonly ISolidColorBrush ErrBorder = new SolidColorBrush(Color.Parse("#9b2c2c"));
    private static readonly ISolidColorBrush AxisBrush = new SolidColorBrush(Color.Parse("#9ca3af"));

    private readonly TraceWaterfall _waterfall;

    public TraceWindow()
    {
        InitializeComponent();
    }

    // 构造函数接收 internal 模型，故也声明为 internal，避免可访问性不一致（与 TopologyWindow 一致）。
    internal TraceWindow(TraceWaterfall waterfall) : this()
    {
        _waterfall = waterfall;
        string shortId = waterfall.RequestId.Length > 8
            ? waterfall.RequestId.Substring(0, 8) + "…"
            : waterfall.RequestId;
        HeaderTextBlock.Text = $"Trace of request {shortId}  -  " +
            $"{waterfall.Spans.Count} span{(waterfall.Spans.Count == 1 ? "" : "s")}  -  '{waterfall.FileName}'";
        Render();
    }

    private void Render()
    {
        var canvas = WaterfallCanvas;
        canvas.Children.Clear();

        var spans = _waterfall.Spans;
        if (spans.Count == 0)
        {
            return;
        }

        // 动态扩展画布高度以容纳所有行。
        canvas.Height = Math.Max(CanvasHeightBase,
            TopMargin + spans.Count * (RowHeight + RowGap) + 10);

        double drawWidth = CanvasWidth - LeftMargin - RightMargin;

        // 计算整条链路的时间范围，用于把每段的起始时刻归一化到画布宽度。
        DateTimeOffset minStart = spans[0].Start;
        DateTimeOffset maxEnd = spans[0].Start.AddMilliseconds(spans[0].DurationMs);
        foreach (var s in spans)
        {
            if (s.Start < minStart) minStart = s.Start;
            var end = s.Start.AddMilliseconds(s.DurationMs);
            if (end > maxEnd) maxEnd = end;
        }
        double totalMs = Math.Max((maxEnd - minStart).TotalMilliseconds, 1.0);

        // 顶部时间刻度（起点 / 总跨度）。
        AddText(canvas, LeftMargin, 6, "+0 ms", AxisBrush);
        AddText(canvas, LeftMargin + drawWidth - 110, 6, $"+{totalMs:0} ms total", AxisBrush);

        for (int i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            double top = TopMargin + i * (RowHeight + RowGap);
            double offsetMs = (span.Start - minStart).TotalMilliseconds;
            double left = LeftMargin + offsetMs / totalMs * drawWidth;
            double w = Math.Max(MinBarWidth, span.DurationMs / totalMs * drawWidth);

            var fill = span.IsError ? ErrFill : OkFill;
            var stroke = span.IsError ? ErrBorder : OkBorder;

            var label = new TextBlock
            {
                Text = span.Label,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var bar = new Border
            {
                Width = w,
                Height = RowHeight,
                CornerRadius = new CornerRadius(8),
                Background = fill,
                BorderBrush = stroke,
                BorderThickness = new Thickness(1.5),
                Child = label,
            };
            Canvas.SetLeft(bar, left);
            Canvas.SetTop(bar, top);
            canvas.Children.Add(bar);
        }
    }

    private static void AddText(Canvas canvas, double x, double y, string text, ISolidColorBrush brush)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = 11,
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        canvas.Children.Add(tb);
    }
}
