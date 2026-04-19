namespace PacketPanda.Core.Http;

public sealed record HttpResponse(int StatusCode, string Body, long ElapsedMilliseconds);
