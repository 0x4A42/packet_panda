using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;

namespace PacketPanda.Desktop.Converters;

/// <summary>Renders an HTTP status code with its reason phrase, e.g. <c>200 OK</c>.</summary>
public sealed partial class StatusCodeTextConverter : IValueConverter
{
    public static readonly StatusCodeTextConverter Instance = new();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelBoundary();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int code) return null;
        if (!Enum.IsDefined(typeof(HttpStatusCode), code))
            return code.ToString();
        var phrase = CamelBoundary().Replace(((HttpStatusCode)code).ToString(), " ");
        return $"{code} {phrase}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
