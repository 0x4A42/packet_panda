using System.Globalization;
using Avalonia.Data.Converters;
using PacketPanda.Core.Http;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Converters;

public sealed class AuthTypeDisplayConverter : IValueConverter
{
    public static readonly AuthTypeDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AuthType t ? t switch
        {
            AuthType.None   => "None",
            AuthType.ApiKey => "API Key",
            AuthType.Bearer => "Bearer Token",
            AuthType.Basic  => "Basic Auth",
            _               => value.ToString()
        } : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ApiKeyTargetDisplayConverter : IValueConverter
{
    public static readonly ApiKeyTargetDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ApiKeyTarget t ? t switch
        {
            ApiKeyTarget.Header     => "Header",
            ApiKeyTarget.QueryParam => "Query Params",
            _                       => value.ToString()
        } : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
