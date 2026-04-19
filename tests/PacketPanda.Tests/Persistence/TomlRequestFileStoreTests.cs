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
    public async Task RoundTrip_PreservesMethodUrlAndBody()
    {
        var request = new HttpRequest(HttpMethod.Post, "https://example.com", """{"a":1}""");

        await _store.SaveAsync(_tempPath, request);
        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Method.Should().Be(HttpMethod.Post);
        loaded.Url.Should().Be("https://example.com");
        loaded.Body.Should().Be("""{"a":1}""");
    }

    [Fact]
    public async Task NullBody_IsOmittedFromFileAndLoadedAsNull()
    {
        var request = new HttpRequest(HttpMethod.Get, "https://example.com", null);

        await _store.SaveAsync(_tempPath, request);
        var content = await File.ReadAllTextAsync(_tempPath);
        var loaded = await _store.LoadAsync(_tempPath);

        content.Should().NotContain("body");
        loaded.Body.Should().BeNull();
    }

    [Fact]
    public async Task EmptyBody_IsTreatedAsNull()
    {
        var request = new HttpRequest(HttpMethod.Get, "https://example.com", "");

        await _store.SaveAsync(_tempPath, request);
        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Body.Should().BeNull();
    }

    [Fact]
    public async Task MultilineJsonBody_RoundTripsCorrectly()
    {
        var body = """
                   {
                     "key": "value",
                     "nested": { "a": 1 }
                   }
                   """;
        var request = new HttpRequest(HttpMethod.Post, "https://example.com", body);

        await _store.SaveAsync(_tempPath, request);
        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Body.Should().Be(body);
    }

    [Fact]
    public async Task Load_MissingFile_ThrowsRequestFileException()
    {
        var act = async () => await _store.LoadAsync("/nonexistent/path/request.toml");

        await act.Should().ThrowAsync<RequestFileException>();
    }

    [Fact]
    public async Task Load_InvalidToml_ThrowsRequestFileException()
    {
        await File.WriteAllTextAsync(_tempPath, "this is not [ valid toml !!!");

        var act = async () => await _store.LoadAsync(_tempPath);

        await act.Should().ThrowAsync<RequestFileException>().WithMessage("Invalid TOML:*");
    }

    [Fact]
    public async Task Load_MissingMethodField_ThrowsRequestFileException()
    {
        await File.WriteAllTextAsync(_tempPath, """url = "https://example.com" """);

        var act = async () => await _store.LoadAsync(_tempPath);

        await act.Should().ThrowAsync<RequestFileException>().WithMessage("*'method'*");
    }

    [Fact]
    public async Task Load_MissingUrlField_ThrowsRequestFileException()
    {
        await File.WriteAllTextAsync(_tempPath, """method = "GET" """);

        var act = async () => await _store.LoadAsync(_tempPath);

        await act.Should().ThrowAsync<RequestFileException>().WithMessage("*'url'*");
    }

    [Fact]
    public async Task Load_UnknownVerb_ThrowsRequestFileException()
    {
        await File.WriteAllTextAsync(_tempPath, """
            method = "PATCH"
            url = "https://example.com"
            """);

        var act = async () => await _store.LoadAsync(_tempPath);

        await act.Should().ThrowAsync<RequestFileException>().WithMessage("*PATCH*");
    }

    [Fact]
    public async Task Load_LowercaseVerb_Succeeds()
    {
        await File.WriteAllTextAsync(_tempPath, """
            method = "get"
            url = "https://example.com"
            """);

        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task Save_WritesMethodAsUppercase()
    {
        var request = new HttpRequest(HttpMethod.Get, "https://example.com");

        await _store.SaveAsync(_tempPath, request);
        var content = await File.ReadAllTextAsync(_tempPath);

        content.Should().Contain("GET");
        content.Should().NotContain("Get");
    }
}
