using System.Text;

namespace PacketPanda.Core.Http;

public static class CurlBuilder
{
    public static string Build(HttpRequest request)
    {
        var sb = new StringBuilder("curl");
        sb.Append($" -X {request.Method.ToString().ToUpperInvariant()}");

        var effectiveUrl = request.Url;
        if (request.Auth is ApiKeyAuth { Target: ApiKeyTarget.QueryParam } qp)
        {
            var separator = effectiveUrl.Contains('?') ? "&" : "?";
            effectiveUrl = $"{effectiveUrl}{separator}{Uri.EscapeDataString(qp.Name)}={Uri.EscapeDataString(qp.Value)}";
        }

        sb.Append($" {Quote(effectiveUrl)}");

        foreach (var h in (request.Headers ?? []).Where(h => h.Enabled))
            sb.Append($" -H {Quote($"{h.Name}: {h.Value}")}");

        switch (request.Auth)
        {
            case BearerAuth bearer:
                sb.Append($" -H {Quote($"Authorization: Bearer {bearer.Token}")}");
                break;
            case BasicAuth basic:
                var encoded = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{basic.Username}:{basic.Password}"));
                sb.Append($" -H 'Authorization: Basic {encoded}'");
                break;
            case ApiKeyAuth { Target: ApiKeyTarget.Header } hdr:
                sb.Append($" -H {Quote($"{hdr.Name}: {hdr.Value}")}");
                break;
        }

        if (request.Method != HttpMethod.Head)
        {
            switch (request.Body)
            {
                case JsonBody { Text: { Length: > 0 } text }:
                    sb.Append(" -H 'Content-Type: application/json'");
                    sb.Append($" --data-raw {Quote(text)}");
                    break;
                case PlainTextBody { Text: { Length: > 0 } text }:
                    sb.Append($" --data-raw {Quote(text)}");
                    break;
                case XmlBody { Text: { Length: > 0 } text }:
                    sb.Append(" -H 'Content-Type: application/xml'");
                    sb.Append($" --data-raw {Quote(text)}");
                    break;
                case FormBody form:
                    foreach (var f in form.Fields.Where(f => f.Enabled))
                        sb.Append($" --data-urlencode {Quote($"{f.Name}={f.Value}")}");
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Wraps <paramref name="s"/> in single quotes, switching to double quotes
    /// if the string contains a single quote (avoids shell escape complexity
    /// and ensures the tokenizer can round-trip the value).
    /// </summary>
    private static string Quote(string s) =>
        s.Contains('\'') ? $"\"{s.Replace("\"", "\\\"")}\"" : $"'{s}'";
}
