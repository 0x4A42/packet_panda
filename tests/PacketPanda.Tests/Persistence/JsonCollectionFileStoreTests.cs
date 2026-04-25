using FluentAssertions;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.Persistence;

public sealed class JsonCollectionFileStoreTests : IDisposable
{
    private readonly string _tempPath = Path.GetTempFileName() + ".json";
    private readonly ICollectionFileStore _store = new JsonCollectionFileStore();

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    private static string MinimalCollection(string name, string itemsJson) => $$"""
        {
          "info": { "name": "{{name}}", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": {{itemsJson}}
        }
        """;

    [Fact]
    public async Task Load_RequestNode_ParsesMethodUrlAndBody()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "abc-123",
                "name": "Get Users",
                "request": {
                  "method": "GET",
                  "header": [],
                  "url": { "raw": "{{baseUrl}}/users" },
                  "body": null
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);

        collection.Name.Should().Be("Test");
        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Id.Should().Be("abc-123");
        req.Name.Should().Be("Get Users");
        req.Method.Should().Be(HttpMethod.Get);
        req.Url.Should().Be("{{baseUrl}}/users");
        req.Body.Should().BeNull();
        req.CollectionFilePath.Should().Be(_tempPath);
    }

    [Fact]
    public async Task Load_RequestNode_WithRawBody_ParsesBody()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "r1",
                "name": "Login",
                "request": {
                  "method": "POST",
                  "header": [],
                  "url": { "raw": "{{baseUrl}}/login" },
                  "body": { "mode": "raw", "raw": "{\"user\":\"x\"}" }
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);

        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Body.Should().Be("{\"user\":\"x\"}");
    }

    [Fact]
    public async Task Load_FolderNode_ParsesNestedItems()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "f1",
                "name": "Auth",
                "item": [
                  {
                    "id": "r1",
                    "name": "Login",
                    "request": { "method": "POST", "header": [], "url": { "raw": "{{base}}/login" }, "body": null },
                    "response": []
                  }
                ]
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);

        var folder = collection.Root.Children.Single().Should().BeOfType<FolderNode>().Subject;
        folder.Name.Should().Be("Auth");
        folder.Children.Single().Should().BeOfType<RequestNode>()
            .Which.Name.Should().Be("Login");
    }

    [Fact]
    public async Task Load_MissingId_AssignsGeneratedGuid()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "name": "No Id",
                "request": { "method": "GET", "header": [], "url": { "raw": "http://x" }, "body": null },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);

        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(req.Id, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Load_MissingInfoName_ThrowsRequestFileException()
    {
        await File.WriteAllTextAsync(_tempPath, """{ "item": [] }""");

        var act = async () => await _store.LoadAsync(_tempPath);

        await act.Should().ThrowAsync<RequestFileException>();
    }

    [Fact]
    public async Task Load_MissingFile_ThrowsRequestFileException()
    {
        var act = async () => await _store.LoadAsync("/nonexistent/path/col.json");

        await act.Should().ThrowAsync<RequestFileException>();
    }

    [Fact]
    public async Task RoundTrip_PreservesAllFields()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("My API", """
            [
              {
                "id": "req-1",
                "name": "Create User",
                "request": {
                  "method": "POST",
                  "header": [],
                  "url": { "raw": "{{base}}/users" },
                  "body": { "mode": "raw", "raw": "{}" }
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);
        await _store.SaveAsync(_tempPath, collection);
        var reloaded = await _store.LoadAsync(_tempPath);

        reloaded.Name.Should().Be("My API");
        var req = reloaded.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Id.Should().Be("req-1");
        req.Method.Should().Be(HttpMethod.Post);
        req.Url.Should().Be("{{base}}/users");
        req.Body.Should().Be("{}");
    }

    [Fact]
    public async Task UpdateRequestAsync_UpdatesOnlyTargetNode()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              { "id": "r1", "name": "A", "request": { "method": "GET", "header": [], "url": { "raw": "http://a" }, "body": null }, "response": [] },
              { "id": "r2", "name": "B", "request": { "method": "GET", "header": [], "url": { "raw": "http://b" }, "body": null }, "response": [] }
            ]
            """));

        await _store.UpdateRequestAsync(_tempPath, "r1", new HttpRequest(HttpMethod.Post, "http://updated", "{\"x\":1}"));
        var reloaded = await _store.LoadAsync(_tempPath);

        var r1 = reloaded.Root.Children.OfType<RequestNode>().First(r => r.Id == "r1");
        r1.Method.Should().Be(HttpMethod.Post);
        r1.Url.Should().Be("http://updated");
        r1.Body.Should().Be("{\"x\":1}");

        var r2 = reloaded.Root.Children.OfType<RequestNode>().First(r => r.Id == "r2");
        r2.Url.Should().Be("http://b");
    }

    [Fact]
    public async Task Load_InvalidJson_ThrowsRequestFileException()
    {
        await File.WriteAllTextAsync(_tempPath, "not valid json {{{{");

        var act = async () => await _store.LoadAsync(_tempPath);

        await act.Should().ThrowAsync<RequestFileException>();
    }

    [Fact]
    public async Task Load_StringUrlFormat_ParsesUrl()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "r1",
                "name": "Plain URL",
                "request": {
                  "method": "GET",
                  "header": [],
                  "url": "https://example.com/path",
                  "body": null
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);

        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Url.Should().Be("https://example.com/path");
    }

    [Fact]
    public async Task UpdateRequestAsync_TargetInNestedFolder_UpdatesCorrectly()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "f1",
                "name": "Auth",
                "item": [
                  { "id": "r1", "name": "Login", "request": { "method": "GET", "header": [], "url": { "raw": "http://old" }, "body": null }, "response": [] }
                ]
              }
            ]
            """));

        await _store.UpdateRequestAsync(_tempPath, "r1", new HttpRequest(HttpMethod.Post, "http://new", null));
        var reloaded = await _store.LoadAsync(_tempPath);

        var folder = reloaded.Root.Children.Single().Should().BeOfType<FolderNode>().Subject;
        var req = folder.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.Url.Should().Be("http://new");
    }
}
