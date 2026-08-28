using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LogAnalyzerClient.Models;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerClient;

public partial class QueryDialog : Window
{
    public QueryDialog()
    {
        InitializeComponent();
    }

    private void QueryButton_Click(object? sender, RoutedEventArgs e)
    {
        // 解析可选的时间范围。用户留空表示不限定；填写但无法解析时给出提示并阻止关闭。
        var (startTime, startError) = TryParseOptionalTime(StartTimeTextBox.Text);
        if (startError is not null)
        {
            ErrorTextBlock.Text = $"Start time: {startError}";
            return;
        }
        var (endTime, endError) = TryParseOptionalTime(EndTimeTextBox.Text);
        if (endError is not null)
        {
            ErrorTextBlock.Text = $"End time: {endError}";
            return;
        }
        if (startTime is not null && endTime is not null && startTime > endTime)
        {
            ErrorTextBlock.Text = "Start time must not be later than end time.";
            return;
        }

        var filter = new QueryFilter
        {
            RequestIdPattern = RequestIdTextBox.Text ?? "",
            ServicePattern = ServiceTextBox.Text ?? "",
            StartTime = startTime,
            EndTime = endTime,
        };
        if (CallCheckBox.IsChecked == true) filter.EventTypes.Add(LogEventTypeEnum.Call);
        if (RequestCheckBox.IsChecked == true) filter.EventTypes.Add(LogEventTypeEnum.Request);
        if (InternalCheckBox.IsChecked == true) filter.EventTypes.Add(LogEventTypeEnum.Internal);
        if (InfoCheckBox.IsChecked == true) filter.Severities.Add(LogSeverityEnum.Info);
        if (WarningCheckBox.IsChecked == true) filter.Severities.Add(LogSeverityEnum.Warning);
        if (ErrorCheckBox.IsChecked == true) filter.Severities.Add(LogSeverityEnum.Error);

        Close(filter);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    /// <summary>
    /// 解析可选的时间输入。返回 (值, 错误信息)：留空返回 (null, null)；
    /// 非空但解析失败返回 (null, 错误信息)；成功返回 (值, null)。
    /// 使用 AssumeUniversal，使得只填写日期时按该日 UTC 0 点处理。
    /// </summary>
    private static (DateTimeOffset? value, string? error) TryParseOptionalTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }
        if (DateTimeOffset.TryParse(
                text.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return (dto, null);
        }
        return (null, $"cannot parse '{text}'. Use e.g. 2026-06-05 or 2026-06-05T17:00:00Z.");
    }
}
