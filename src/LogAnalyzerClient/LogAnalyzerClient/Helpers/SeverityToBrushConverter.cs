using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace LogAnalyzerClient.Helpers
{
    public class SeverityToBrushConverter : IValueConverter
    {
        private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
        private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
        private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#EF4444"));
        private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#6B7280"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value as string)?.ToLowerInvariant() switch
            {
                "info" => InfoBrush,
                "warning" => WarningBrush,
                "error" => ErrorBrush,
                _ => DefaultBrush,
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
