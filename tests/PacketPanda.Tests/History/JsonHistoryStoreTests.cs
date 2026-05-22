using FluentAssertions;
using PacketPanda.Core.History;
using PacketPanda.Core.Http;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.History;

public sealed class JsonHistoryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pp-history-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static HistoryEntry Entry(string url) => new(
        DateTimeOffset.UtcNow, HttpMethod.Get, url,
        new[] { new HttpHeader("Accept", "application/json") },
        new JsonBody("{}"), new BearerAuth("tok"),
        200, "ok", new[] { new HttpHeader("Server", "test") }, 12, null);

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        var store = new JsonHistoryStore(_path);

        (await store.LoadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_ThenLoad_ReturnsEntriesNewestFirst()
    {
        var store = new JsonHistoryStore(_path);

        await store.AppendAsync(Entry("https://first.test"));
        await store.AppendAsync(Entry("https://second.test"));

        var entries = await store.LoadAsync();
        entries.Should().HaveCount(2);
        entries[0].Url.Should().Be("https://second.test");
        entries[1].Url.Should().Be("https://first.test");
    }

    [Fact]
    public async Task AppendAsync_RoundTripsBodyAndAuthDiscriminatedTypes()
    {
        var store = new JsonHistoryStore(_path);

        await store.AppendAsync(Entry("https://rt.test"));

        var loaded = (await store.LoadAsync())[0];
        loaded.Body.Should().BeOfType<JsonBody>();
        loaded.Auth.Should().BeOfType<BearerAuth>();
        ((BearerAuth)loaded.Auth).Token.Should().Be("tok");
        loaded.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task AppendAsync_PrunesToNewest200()
    {
        var store = new JsonHistoryStore(_path);

        for (var i = 0; i < 205; i++)
            await store.AppendAsync(Entry($"https://e{i}.test"));

        var entries = await store.LoadAsync();
        entries.Should().HaveCount(200);
        entries[0].Url.Should().Be("https://e204.test");
        entries[199].Url.Should().Be("https://e5.test");
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmpty()
    {
        await File.WriteAllTextAsync(_path, "{ this is not valid json");
        var store = new JsonHistoryStore(_path);

        (await store.LoadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        var store = new JsonHistoryStore(_path);
        await store.AppendAsync(Entry("https://x.test"));

        await store.ClearAsync();

        (await store.LoadAsync()).Should().BeEmpty();
    }
}
