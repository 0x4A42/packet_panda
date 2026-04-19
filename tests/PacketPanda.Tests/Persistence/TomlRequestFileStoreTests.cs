using FluentAssertions;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.Persistence;

public class TomlRequestFileStoreTests : IDisposable
{
    private readonly string _tempPath = Path.GetTempFileName() + ".toml";
    private readonly IRequestFileStore _store = new TomlRequestFileStore();

    public void Dispose()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [Fact]
    public async Task RoundTrip_preserves_method_url_and_body()
    {
        var request = new HttpRequest(HttpMethod.Post, "https://example.com", """{"a":1}""");

        await _store.SaveAsync(_tempPath, request);
        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Method.Should().Be(HttpMethod.Post);
        loaded.Url.Should().Be("https://example.com");
        loaded.Body.Should().Be("""{"a":1}""");
    }
}
