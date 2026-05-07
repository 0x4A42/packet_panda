using System.Globalization;
using Avalonia.Data.Converters;

namespace PacketPanda.Desktop.Converters;

public sealed class BoolToPassFailConverter : IValueConverter
{
    public static readonly BoolToPassFailConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "✓" : "✗";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
