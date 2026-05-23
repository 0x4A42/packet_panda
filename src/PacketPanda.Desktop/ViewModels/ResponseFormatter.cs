using System.Text.Json;
using PacketPanda.Core.Http;

namespace PacketPanda.Desktop.ViewModels;

internal static class ResponseFormatter
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    internal static string? MaybePrettyPrint(string? body, IReadOnlyList<HttpHeader>? headers)
    {
        if (string.IsNullOrEmpty(body)) return body;
        var isJson = headers?.Any(h =>
            h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
            h.Value.Contains("application/json", StringComparison.OrdinalIgnoreCase)) ?? false;
        if (!isJson) return body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(doc.RootElement, IndentedOptions);
        }
        catch (JsonException) { return body; }
    }
}
