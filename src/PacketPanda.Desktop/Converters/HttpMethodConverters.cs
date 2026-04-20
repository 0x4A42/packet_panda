using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PacketPanda.Desktop.Converters;

public sealed class IsNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class HttpMethodBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string method ? GetBrush(method) : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush GetBrush(string method) => method switch
    {
        "GET"    => new SolidColorBrush(Color.Parse("#61AFFE")),
        "POST"   => new SolidColorBrush(Color.Parse("#49CC90")),
        "PUT"    => new SolidColorBrush(Color.Parse("#FCA130")),
        "DELETE" => new SolidColorBrush(Color.Parse("#F93E3E")),
        "PATCH"  => new SolidColorBrush(Color.Parse("#50E3C2")),
        _        => new SolidColorBrush(Color.Parse("#888888"))
    };
}
