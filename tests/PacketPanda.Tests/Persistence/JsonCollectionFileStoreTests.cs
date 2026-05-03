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

    [Fact]
    public async Task Load_RequestNode_ParsesHeaders()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "r1", "name": "R",
                "request": {
                  "method": "GET",
                  "header": [
                    { "key": "X-Foo", "value": "bar" },
                    { "key": "X-Disabled", "value": "skip", "disabled": true }
                  ],
                  "url": { "raw": "https://example.com" },
                  "body": null
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);
        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;

        req.Headers.Should().HaveCount(2);
        req.Headers[0].Should().Be(new HttpHeader("X-Foo", "bar", true));
        req.Headers[1].Should().Be(new HttpHeader("X-Disabled", "skip", false));
    }

    [Fact]
    public async Task Load_RequestNode_MissingAuth_ReturnsNoAuth()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "r1", "name": "R",
                "request": {
                  "method": "GET", "header": [],
                  "url": { "raw": "https://example.com" }, "body": null
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);
        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Auth.Should().BeOfType<NoAuth>();
    }

    [Fact]
    public async Task Load_RequestNode_ParsesBearerAuth()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "r1", "name": "R",
                "request": {
                  "method": "GET", "header": [],
                  "url": { "raw": "https://example.com" }, "body": null,
                  "auth": {
                    "type": "bearer",
                    "bearer": [{ "key": "token", "value": "tok123", "type": "string" }]
                  }
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);
        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Auth.Should().Be(new BearerAuth("tok123"));
    }

    [Fact]
    public async Task Load_RequestNode_ParsesApiKeyAuth()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "r1", "name": "R",
                "request": {
                  "method": "GET", "header": [],
                  "url": { "raw": "https://example.com" }, "body": null,
                  "auth": {
                    "type": "apikey",
                    "apikey": [
                      { "key": "key", "value": "X-API-Key", "type": "string" },
                      { "key": "value", "value": "mykey", "type": "string" },
                      { "key": "in", "value": "header", "type": "string" }
                    ]
                  }
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);
        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Auth.Should().Be(new ApiKeyAuth("X-API-Key", "mykey", ApiKeyTarget.Header));
    }

    [Fact]
    public async Task Load_RequestNode_ParsesBasicAuth()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "r1", "name": "R",
                "request": {
                  "method": "GET", "header": [],
                  "url": { "raw": "https://example.com" }, "body": null,
                  "auth": {
                    "type": "basic",
                    "basic": [
                      { "key": "username", "value": "admin", "type": "string" },
                      { "key": "password", "value": "s3cr3t", "type": "string" }
                    ]
                  }
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);
        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Auth.Should().Be(new BasicAuth("admin", "s3cr3t"));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesHeaders()
    {
        var col = new Collection("C", _tempPath,
            new FolderNode("root", "C", [
                new RequestNode("r1", "R", _tempPath, HttpMethod.Get, "https://example.com", null,
                    [new HttpHeader("X-Foo", "bar"), new HttpHeader("X-Off", "skip", Enabled: false)],
                    new NoAuth(), Array.Empty<QueryParam>())
            ]));
        await _store.SaveAsync(_tempPath, col);
        var loaded = await _store.LoadAsync(_tempPath);

        var req = loaded.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Headers.Should().HaveCount(2);
        req.Headers[0].Should().Be(new HttpHeader("X-Foo", "bar", true));
        req.Headers[1].Should().Be(new HttpHeader("X-Off", "skip", false));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesBearerAuth()
    {
        var col = new Collection("C", _tempPath,
            new FolderNode("root", "C", [
                new RequestNode("r1", "R", _tempPath, HttpMethod.Get, "https://example.com", null,
                    Array.Empty<HttpHeader>(), new BearerAuth("tok"), Array.Empty<QueryParam>())
            ]));
        await _store.SaveAsync(_tempPath, col);
        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Root.Children.Single().Should().BeOfType<RequestNode>()
            .Which.Auth.Should().Be(new BearerAuth("tok"));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesBasicAuth()
    {
        var col = new Collection("C", _tempPath,
            new FolderNode("root", "C", [
                new RequestNode("r1", "R", _tempPath, HttpMethod.Get, "https://example.com", null,
                    Array.Empty<HttpHeader>(), new BasicAuth("u", "p"), Array.Empty<QueryParam>())
            ]));
        await _store.SaveAsync(_tempPath, col);
        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Root.Children.Single().Should().BeOfType<RequestNode>()
            .Which.Auth.Should().Be(new BasicAuth("u", "p"));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesApiKeyAuthWithQueryParamTarget()
    {
        var col = new Collection("C", _tempPath,
            new FolderNode("root", "C", [
                new RequestNode("r1", "R", _tempPath, HttpMethod.Get, "https://example.com", null,
                    Array.Empty<HttpHeader>(), new ApiKeyAuth("api_key", "secret", ApiKeyTarget.QueryParam), Array.Empty<QueryParam>())
            ]));
        await _store.SaveAsync(_tempPath, col);
        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Root.Children.Single().Should().BeOfType<RequestNode>()
            .Which.Auth.Should().Be(new ApiKeyAuth("api_key", "secret", ApiKeyTarget.QueryParam));
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_NoAuth_OmitsAuthKeyAndReturnsNoAuth()
    {
        var col = new Collection("C", _tempPath,
            new FolderNode("root", "C", [
                new RequestNode("r1", "R", _tempPath, HttpMethod.Get, "https://example.com", null,
                    Array.Empty<HttpHeader>(), new NoAuth(), Array.Empty<QueryParam>())
            ]));
        await _store.SaveAsync(_tempPath, col);

        var json = await File.ReadAllTextAsync(_tempPath);
        json.Should().NotContain("\"auth\"");

        var loaded = await _store.LoadAsync(_tempPath);
        loaded.Root.Children.Single().Should().BeOfType<RequestNode>()
            .Which.Auth.Should().BeOfType<NoAuth>();
    }

    [Fact]
    public async Task UpdateRequestAsync_PartialRequest_PreservesExistingHeadersAndAuth()
    {
        var col = new Collection("C", _tempPath,
            new FolderNode("root", "C", [
                new RequestNode("r1", "R", _tempPath, HttpMethod.Get, "https://example.com", null,
                    [new HttpHeader("X-Keep", "v")], new BearerAuth("tok"), Array.Empty<QueryParam>())
            ]));
        await _store.SaveAsync(_tempPath, col);

        await _store.UpdateRequestAsync(_tempPath, "r1",
            new HttpRequest(HttpMethod.Post, "https://updated", null));

        var loaded = await _store.LoadAsync(_tempPath);
        var req = loaded.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.Method.Should().Be(HttpMethod.Post);
        req.Url.Should().Be("https://updated");
        req.Headers.Should().ContainSingle(h => h.Name == "X-Keep");
        req.Auth.Should().Be(new BearerAuth("tok"));
    }

    [Fact]
    public async Task Load_RequestNode_ParsesQueryParams()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "r1", "name": "Search",
                "request": {
                  "method": "GET",
                  "header": [],
                  "url": {
                    "raw": "https://api.example.com/search?q=hello",
                    "query": [
                      { "key": "q", "value": "hello" },
                      { "key": "debug", "value": "1", "disabled": true }
                    ]
                  },
                  "body": null
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);

        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.QueryParams.Should().HaveCount(2);
        req.QueryParams[0].Key.Should().Be("q");
        req.QueryParams[0].Value.Should().Be("hello");
        req.QueryParams[0].Enabled.Should().BeTrue();
        req.QueryParams[1].Key.Should().Be("debug");
        req.QueryParams[1].Value.Should().Be("1");
        req.QueryParams[1].Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Load_RequestNode_LegacyStringUrl_HasEmptyQueryParams()
    {
        await File.WriteAllTextAsync(_tempPath, MinimalCollection("Test", """
            [
              {
                "id": "r1", "name": "Get",
                "request": {
                  "method": "GET",
                  "header": [],
                  "url": "https://api.example.com/users",
                  "body": null
                },
                "response": []
              }
            ]
            """));

        var collection = await _store.LoadAsync(_tempPath);

        var req = collection.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.QueryParams.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_ThenLoad_RoundTripsQueryParams()
    {
        var queryParams = new List<QueryParam>
        {
            new QueryParam("q", "hello", true),
            new QueryParam("debug", "1", false)
        };

        var root = new FolderNode(Guid.NewGuid().ToString(), "Col", new List<CollectionNode>
        {
            new RequestNode("r1", "Search", _tempPath, HttpMethod.Get,
                "https://api.example.com/search?q=hello", null,
                Array.Empty<HttpHeader>(), new NoAuth(), queryParams)
        });
        var collection = new Collection("Col", _tempPath, root);
        await _store.SaveAsync(_tempPath, collection);

        var loaded = await _store.LoadAsync(_tempPath);
        var req = loaded.Root.Children.Single().Should().BeOfType<RequestNode>().Subject;
        req.QueryParams.Should().HaveCount(2);
        req.QueryParams[0].Key.Should().Be("q");
        req.QueryParams[0].Value.Should().Be("hello");
        req.QueryParams[0].Enabled.Should().BeTrue();
        req.QueryParams[1].Key.Should().Be("debug");
        req.QueryParams[1].Value.Should().Be("1");
        req.QueryParams[1].Enabled.Should().BeFalse();
    }
}
