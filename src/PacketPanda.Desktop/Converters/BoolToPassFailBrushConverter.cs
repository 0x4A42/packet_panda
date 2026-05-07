using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PacketPanda.Desktop.Converters;

public sealed class BoolToPassFailBrushConverter : IValueConverter
{
    public static readonly BoolToPassFailBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.SeaGreen : Brushes.IndianRed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
