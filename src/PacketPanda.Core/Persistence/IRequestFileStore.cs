using PacketPanda.Core.Http;

namespace PacketPanda.Core.Persistence;

public interface IRequestFileStore
{
    Task<HttpRequest> LoadAsync(string path);
    Task SaveAsync(string path, HttpRequest request);
}
