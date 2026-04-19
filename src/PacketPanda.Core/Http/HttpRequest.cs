namespace PacketPanda.Core.Http;

public sealed record HttpRequest(HttpMethod Method, string Url, string? Body = null);
