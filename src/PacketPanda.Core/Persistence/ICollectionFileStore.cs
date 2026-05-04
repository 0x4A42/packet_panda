using PacketPanda.Core.Http;

namespace PacketPanda.Core.Persistence;

public interface ICollectionFileStore
{
    Task<Collection> LoadAsync(string path);
    Task SaveAsync(string path, Collection collection);
    Task UpdateRequestAsync(string collectionPath, string requestId, HttpRequest updated,
        string? preRequestScript = null, string? postResponseScript = null);
}
