using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace LogAnalyzerClient.Converters;

public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Info" => Brush.Parse("#2563EB"),
            "Warning" => Brush.Parse("#F59E0B"),
            "Error" => Brush.Parse("#DC2626"),
            _ => Brushes.Transparent
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}