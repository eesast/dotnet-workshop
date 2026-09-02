using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace LogAnalyzerClient.Converters
{
    /// <summary>
    /// Converts a severity display string ("Info" / "Warning" / "Error") to the
    /// background brush of the severity "pill" rendered in the results table:
    /// Info -> blue, Warning -> orange, Error -> red.
    /// </summary>
    public sealed class SeverityBrushConverter : IValueConverter
    {
        private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#1565C0"));
        private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#EF6C00"));
        private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#C62828"));
        private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse("#757575"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value as string)?.ToLowerInvariant() switch
            {
                "info" => InfoBrush,
                "warning" => WarningBrush,
                "error" => ErrorBrush,
                _ => UnknownBrush,
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
