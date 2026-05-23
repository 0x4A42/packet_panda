using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PacketPanda.Desktop.Converters;

public sealed class StatusCodeBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int code)
            return Brushes.Transparent;

        return (code / 100) switch
        {
            2 => new SolidColorBrush(Color.Parse("#4CAF50")),
            3 => new SolidColorBrush(Color.Parse("#00ACC1")),
            4 or 5 => new SolidColorBrush(Color.Parse("#EF5350")),
            _ => Brushes.Transparent
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
