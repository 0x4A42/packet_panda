using System.Text;

namespace PacketPanda.Core.Http;

public static class CurlBuilder
{
    public static string Build(HttpRequest request)
    {
        var sb = new StringBuilder("curl");
        sb.Append($" -X {request.Method.ToString().ToUpperInvariant()}");
        sb.Append($" '{Escape(request.Url)}'");

        foreach (var h in (request.Headers ?? []).Where(h => h.Enabled))
            sb.Append($" -H '{Escape(h.Name)}: {Escape(h.Value)}'");

        switch (request.Auth)
        {
            case BearerAuth bearer:
                sb.Append($" -H 'Authorization: Bearer {Escape(bearer.Token)}'");
                break;
            case BasicAuth basic:
                var encoded = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{basic.Username}:{basic.Password}"));
                sb.Append($" -H 'Authorization: Basic {encoded}'");
                break;
            case ApiKeyAuth { Target: ApiKeyTarget.Header } hdr:
                sb.Append($" -H '{Escape(hdr.Name)}: {Escape(hdr.Value)}'");
                break;
        }

        if (request.Method != HttpMethod.Head)
        {
            switch (request.Body)
            {
                case JsonBody { Text: { Length: > 0 } text }:
                    sb.Append(" -H 'Content-Type: application/json'");
                    sb.Append($" --data-raw '{Escape(text)}'");
                    break;
                case PlainTextBody { Text: { Length: > 0 } text }:
                    sb.Append($" --data-raw '{Escape(text)}'");
                    break;
                case XmlBody { Text: { Length: > 0 } text }:
                    sb.Append(" -H 'Content-Type: application/xml'");
                    sb.Append($" --data-raw '{Escape(text)}'");
                    break;
                case FormBody form:
                    foreach (var f in form.Fields.Where(f => f.Enabled))
                        sb.Append($" --data-urlencode '{Escape(f.Name)}={Escape(f.Value)}'");
                    break;
            }
        }

        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("'", "'\\''");
}
