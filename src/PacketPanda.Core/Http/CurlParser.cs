using System.Text;

namespace PacketPanda.Core.Http;

public static class CurlParser
{
    public static ParsedCurlRequest? Parse(string curlString)
    {
        if (string.IsNullOrWhiteSpace(curlString)) return null;

        var tokens = Tokenize(curlString);
        if (tokens.Count == 0 || tokens[0] != "curl") return null;

        string? url = null;
        var method = HttpMethod.Get;
        var headers = new List<HttpHeader>();
        string? rawBody = null;
        var hasBody = false;

        for (var i = 1; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t)
            {
                case "-X" or "--request":
                    if (++i < tokens.Count && Enum.TryParse<HttpMethod>(tokens[i], ignoreCase: true, out var m))
                        method = m;
                    break;

                case "-H" or "--header":
                    if (++i < tokens.Count)
                    {
                        var header = tokens[i];
                        var colon = header.IndexOf(':');
                        if (colon > 0)
                            headers.Add(new HttpHeader(
                                header[..colon].Trim(),
                                header[(colon + 1)..].Trim()));
                    }
                    break;

                case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii":
                    if (++i < tokens.Count) { rawBody = tokens[i]; hasBody = true; }
                    break;

                case "--url":
                    if (++i < tokens.Count) url = tokens[i];
                    break;

                default:
                    if (!t.StartsWith('-') && url is null)
                        url = t;
                    break;
            }
        }

        if (url is null) return null;
        if (hasBody && method == HttpMethod.Get) method = HttpMethod.Post;

        var contentType = headers
            .FirstOrDefault(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            ?.Value ?? string.Empty;

        BodyConfig body;
        if (rawBody is not null)
        {
            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                body = new JsonBody(rawBody);
            else if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                var fields = rawBody.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .Select(p => new FormField(
                        Uri.UnescapeDataString(p[0]),
                        Uri.UnescapeDataString(p[1]),
                        true))
                    .ToList();
                body = new FormBody(fields);
            }
            else if (contentType.Contains("application/xml", StringComparison.OrdinalIgnoreCase))
                body = new XmlBody(rawBody);
            else
                body = new PlainTextBody(rawBody);
        }
        else
            body = new NoBody();

        return new ParsedCurlRequest(url, method, headers, body);
    }

    private static IReadOnlyList<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
            if (i >= input.Length) break;

            var sb = new StringBuilder();
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
            {
                if (input[i] is '\'' or '"')
                {
                    var quote = input[i++];
                    while (i < input.Length && input[i] != quote)
                    {
                        if (input[i] == '\\' && i + 1 < input.Length) i++;
                        sb.Append(input[i++]);
                    }
                    if (i < input.Length) i++; // skip closing quote
                }
                else if (char.IsWhiteSpace(input[i]))
                    break;
                else
                    sb.Append(input[i++]);
            }
            tokens.Add(sb.ToString());
        }
        return tokens;
    }
}
