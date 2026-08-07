using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LogParser.Models;

namespace LogAnalyzerClient.Converters
{
    /// <summary>
    /// 把日志等级映射为高亮背景色（T5.1.b.a）：Info=蓝、Warning=橙、Error=红。
    /// 用于结果表格 Severity 列圆角胶囊的 Background 绑定。
    /// </summary>
    public sealed class SeverityToBrushConverter : IValueConverter
    {
        private static readonly IBrush SevInfo = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));
        private static readonly IBrush SevWarning = new SolidColorBrush(Color.FromRgb(0xDD, 0x6B, 0x20));
        private static readonly IBrush SevError = new SolidColorBrush(Color.FromRgb(0xC5, 0x30, 0x30));
        private static readonly IBrush Fallback = new SolidColorBrush(Colors.Gray);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is LogSeverity severity)
            {
                return severity switch
                {
                    LogSeverity.Info => SevInfo,
                    LogSeverity.Warning => SevWarning,
                    LogSeverity.Error => SevError,
                    _ => Fallback,
                };
            }
            return Fallback;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
