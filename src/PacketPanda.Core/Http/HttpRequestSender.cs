using System.Diagnostics;
using System.Text;
using PacketPanda.Core.Persistence;
using SystemHttpMethod = System.Net.Http.HttpMethod;

namespace PacketPanda.Core.Http;

public sealed class HttpRequestSender : IHttpRequestSender, IDisposable
{
    private readonly HttpClient _httpClient;

    public HttpRequestSender(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static HttpRequestSender Create(AppSettings settings)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = settings.FollowRedirects,
            ServerCertificateCustomValidationCallback = settings.SslVerification
                ? null
                : HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
        };
        return new HttpRequestSender(client);
    }

    public void Dispose() => _httpClient.Dispose();

    public async Task<SendResult> SendAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage message;
        try
        {
            message = BuildMessage(request);
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException or ArgumentException)
        {
            return new SendResult.Error(ex.Message);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var responseHeaders = response.Headers
                .Concat(response.Content.Headers)
                .Select(h => new HttpHeader(h.Key, string.Join(", ", h.Value)))
                .ToList();

            return new SendResult.Ok(new HttpResponse(
                (int)response.StatusCode, body, stopwatch.ElapsedMilliseconds, responseHeaders));
        }
        catch (HttpRequestException ex)
        {
            return new SendResult.Error(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // Thrown by HttpClient for malformed/relative URIs with no BaseAddress.
            return new SendResult.Error(ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout (HttpClient.Timeout). True user cancellation rethrows.
            return new SendResult.Error(ex.Message);
        }
        finally
        {
            message.Dispose();
        }
    }

    private static HttpRequestMessage BuildMessage(HttpRequest request)
    {
        var method = request.Method switch
        {
            HttpMethod.Get     => SystemHttpMethod.Get,
            HttpMethod.Post    => SystemHttpMethod.Post,
            HttpMethod.Put     => SystemHttpMethod.Put,
            HttpMethod.Delete  => SystemHttpMethod.Delete,
            HttpMethod.Patch   => SystemHttpMethod.Patch,
            HttpMethod.Head    => SystemHttpMethod.Head,
            HttpMethod.Options => SystemHttpMethod.Options,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Method, "Unsupported method")
        };

        var url = request.Url;
        if (request.Auth is ApiKeyAuth { Target: ApiKeyTarget.QueryParam } qpAuth)
        {
            var sep = url.Contains('?') ? '&' : '?';
            url = $"{url}{sep}{Uri.EscapeDataString(qpAuth.Name)}={Uri.EscapeDataString(qpAuth.Value)}";
        }

        if (request.QueryParams is { Count: > 0 })
        {
            var enabled = request.QueryParams
                .Where(p => p.Enabled && !string.IsNullOrEmpty(p.Key))
                .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")
                .ToList();
            if (enabled.Count > 0)
            {
                var sep = url.Contains('?') ? '&' : '?';
                url = $"{url}{sep}{string.Join("&", enabled)}";
            }
        }

        var message = new HttpRequestMessage(method, url);

        if (request.Headers is not null)
            foreach (var h in request.Headers.Where(h => h.Enabled))
                message.Headers.TryAddWithoutValidation(h.Name, h.Value);

        switch (request.Auth)
        {
            case BearerAuth bearer:
                message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer.Token}");
                break;
            case BasicAuth basic:
                var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basic.Username}:{basic.Password}"));
                message.Headers.TryAddWithoutValidation("Authorization", $"Basic {creds}");
                break;
            case ApiKeyAuth { Target: ApiKeyTarget.Header } hdrAuth:
                message.Headers.TryAddWithoutValidation(hdrAuth.Name, hdrAuth.Value);
                break;
        }

        if (request.Method != HttpMethod.Head)
        {
            switch (request.Body)
            {
                case JsonBody json when !string.IsNullOrEmpty(json.Text):
                    message.Content = new StringContent(json.Text, Encoding.UTF8, "application/json");
                    break;
                case PlainTextBody text when !string.IsNullOrEmpty(text.Text):
                    message.Content = new StringContent(text.Text, Encoding.UTF8, "text/plain");
                    break;
                case XmlBody xml when !string.IsNullOrEmpty(xml.Text):
                    message.Content = new StringContent(xml.Text, Encoding.UTF8, "application/xml");
                    break;
                case FormBody form:
                    var encoded = form.Fields
                        .Where(f => f.Enabled)
                        .Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
                        .ToList();
                    message.Content = new FormUrlEncodedContent(encoded);
                    break;
                case MultipartBody multipart:
                    var multipartContent = new MultipartFormDataContent();
                    // .NET quotes the boundary by default; strip quotes so servers that
                    // don't accept RFC 2046 quoted-string boundaries parse fields correctly.
                    var boundaryParam = multipartContent.Headers.ContentType!.Parameters
                        .First(p => p.Name == "boundary");
                    boundaryParam.Value = boundaryParam.Value!.Trim('"');
                    foreach (var part in multipart.Parts.Where(p => p.Enabled))
                    {
                        if (part.Type == MultipartPartType.File)
                        {
                            if (!File.Exists(part.Value)) continue;
                            var bytes = File.ReadAllBytes(part.Value);
                            var fileContent = new ByteArrayContent(bytes);
                            fileContent.Headers.ContentType =
                                new System.Net.Http.Headers.MediaTypeHeaderValue(MimeTypeFor(part.Value));
                            // Write Content-Disposition as a raw string to avoid .NET encoding
                            // the name/filename with RFC 5987 (name*=UTF-8'') which many servers
                            // don't recognise as the standard `name` parameter.
                            fileContent.Headers.TryAddWithoutValidation(
                                "Content-Disposition",
                                $"form-data; name=\"{part.Name}\"; filename=\"{Path.GetFileName(part.Value)}\"");
                            multipartContent.Add(fileContent);
                        }
                        else
                        {
                            var textContent = new StringContent(part.Value, Encoding.UTF8);
                            textContent.Headers.TryAddWithoutValidation(
                                "Content-Disposition",
                                $"form-data; name=\"{part.Name}\"");
                            multipartContent.Add(textContent);
                        }
                    }
                    message.Content = multipartContent;
                    break;
            }
        }

        return message;
    }

    public static string MimeTypeFor(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".json" => "application/json",
            ".xml"  => "application/xml",
            ".txt"  => "text/plain",
            ".html" => "text/html",
            ".csv"  => "text/csv",
            ".png"  => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif"  => "image/gif",
            ".pdf"  => "application/pdf",
            ".zip"  => "application/zip",
            _       => "application/octet-stream"
        };
    }
}
