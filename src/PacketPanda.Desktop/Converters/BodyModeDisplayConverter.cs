using System.Globalization;
using Avalonia.Data.Converters;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Converters;

public sealed class BodyModeDisplayConverter : IValueConverter
{
    public static readonly BodyModeDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BodyMode mode ? mode switch
        {
            BodyMode.None      => "None",
            BodyMode.Json      => "JSON",
            BodyMode.Text      => "Text",
            BodyMode.Xml       => "XML",
            BodyMode.Form      => "Form",
            BodyMode.Multipart => "Multipart",
            _                  => value.ToString()
        } : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
