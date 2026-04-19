namespace PacketPanda.Core.Http;

public interface IHttpRequestSender
{
    Task<SendResult> SendAsync(HttpRequest request, CancellationToken cancellationToken = default);
}
