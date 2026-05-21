using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace PacketPanda.Desktop.Converters;

/// <summary>Converts a <c>TreeViewItem.Level</c> into a left margin so nested rows indent.</summary>
public sealed class TreeIndentConverter : IValueConverter
{
    public static readonly TreeIndentConverter Instance = new();

    private const double IndentPerLevel = 14.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value is int i ? i : 0;
        return new Thickness(level * IndentPerLevel, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
