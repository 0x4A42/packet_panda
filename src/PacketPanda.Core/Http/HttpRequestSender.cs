using System.Diagnostics;
using System.Text;
using SystemHttpMethod = System.Net.Http.HttpMethod;

namespace PacketPanda.Core.Http;

public sealed class HttpRequestSender : IHttpRequestSender
{
    private readonly HttpClient _httpClient;

    public HttpRequestSender(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

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
            return new SendResult.Ok(new HttpResponse((int)response.StatusCode, body, stopwatch.ElapsedMilliseconds));
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
            HttpMethod.Get => SystemHttpMethod.Get,
            HttpMethod.Post => SystemHttpMethod.Post,
            HttpMethod.Put => SystemHttpMethod.Put,
            HttpMethod.Delete => SystemHttpMethod.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Method, "Unsupported method")
        };

        var url = request.Url;
        if (request.Auth is ApiKeyAuth { Target: ApiKeyTarget.QueryParam } qpAuth)
        {
            var sep = url.Contains('?') ? '&' : '?';
            url = $"{url}{sep}{Uri.EscapeDataString(qpAuth.Name)}={Uri.EscapeDataString(qpAuth.Value)}";
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

        if (!string.IsNullOrEmpty(request.Body))
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");

        return message;
    }
}
