using PacketPanda.Core.Http;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Core.History;

/// <summary>
/// One recorded HTTP send. All values are resolved — environment variables are
/// already substituted and auth secrets are stored as-is.
/// </summary>
public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    HttpMethod Method,
    string Url,
    IReadOnlyList<HttpHeader> Headers,
    BodyConfig Body,
    AuthConfig Auth,
    int? StatusCode,
    string? ResponseBody,
    IReadOnlyList<HttpHeader>? ResponseHeaders,
    long? ElapsedMilliseconds,
    string? ErrorMessage);
