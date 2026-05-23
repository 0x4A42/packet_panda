using FluentAssertions;
using PacketPanda.Core.Http;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.Http;

public sealed class CurlBuilderTests
{
    [Fact]
    public void Build_SimpleGet_ProducesBasicCurl()
    {
        var request = new HttpRequest(HttpMethod.Get, "https://example.com/api");

        var result = CurlBuilder.Build(request);

        result.Should().Be("curl -X GET 'https://example.com/api'");
    }

    [Fact]
    public void Build_PostWithJsonBody_IncludesContentTypeAndData()
    {
        var request = new HttpRequest(
            HttpMethod.Post,
            "https://example.com/api",
            new JsonBody("{\"key\":\"value\"}"));

        var result = CurlBuilder.Build(request);

        result.Should().Contain("-X POST");
        result.Should().Contain("-H 'Content-Type: application/json'");
        result.Should().Contain("--data-raw '{\"key\":\"value\"}'");
    }

    [Fact]
    public void Build_WithEnabledHeader_IncludesHeader()
    {
        var request = new HttpRequest(
            HttpMethod.Get,
            "https://example.com",
            Headers: [new HttpHeader("Accept", "application/json", true)]);

        var result = CurlBuilder.Build(request);

        result.Should().Contain("-H 'Accept: application/json'");
    }

    [Fact]
    public void Build_WithDisabledHeader_OmitsHeader()
    {
        var request = new HttpRequest(
            HttpMethod.Get,
            "https://example.com",
            Headers: [new HttpHeader("X-Disabled", "value", false)]);

        var result = CurlBuilder.Build(request);

        result.Should().NotContain("X-Disabled");
    }

    [Fact]
    public void Build_BearerAuth_IncludesAuthorizationHeader()
    {
        var request = new HttpRequest(
            HttpMethod.Get,
            "https://example.com",
            Auth: new BearerAuth("mytoken"));

        var result = CurlBuilder.Build(request);

        result.Should().Contain("-H 'Authorization: Bearer mytoken'");
    }

    [Fact]
    public void Build_BasicAuth_IncludesBase64Header()
    {
        var request = new HttpRequest(
            HttpMethod.Get,
            "https://example.com",
            Auth: new BasicAuth("user", "pass"));

        var result = CurlBuilder.Build(request);

        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:pass"));
        result.Should().Contain($"-H 'Authorization: Basic {encoded}'");
    }

    [Fact]
    public void Build_ApiKeyHeaderAuth_IncludesHeader()
    {
        var request = new HttpRequest(
            HttpMethod.Get,
            "https://example.com",
            Auth: new ApiKeyAuth("X-Api-Key", "secret", ApiKeyTarget.Header));

        var result = CurlBuilder.Build(request);

        result.Should().Contain("-H 'X-Api-Key: secret'");
    }

    [Fact]
    public void Build_FormBody_UsesDataUrlencode()
    {
        var request = new HttpRequest(
            HttpMethod.Post,
            "https://example.com",
            new FormBody([new FormField("name", "Alice", true), new FormField("age", "30", true)]));

        var result = CurlBuilder.Build(request);

        result.Should().Contain("--data-urlencode 'name=Alice'");
        result.Should().Contain("--data-urlencode 'age=30'");
    }

    [Fact]
    public void Build_FormBody_SkipsDisabledFields()
    {
        var request = new HttpRequest(
            HttpMethod.Post,
            "https://example.com",
            new FormBody([new FormField("active", "yes", true), new FormField("hidden", "no", false)]));

        var result = CurlBuilder.Build(request);

        result.Should().Contain("--data-urlencode 'active=yes'");
        result.Should().NotContain("hidden");
    }

    [Fact]
    public void Build_HeadMethod_OmitsBody()
    {
        var request = new HttpRequest(
            HttpMethod.Head,
            "https://example.com",
            new JsonBody("{\"key\":\"val\"}"));

        var result = CurlBuilder.Build(request);

        result.Should().NotContain("--data-raw");
    }

    [Fact]
    public void Build_SingleQuoteInUrl_IsEscaped()
    {
        var request = new HttpRequest(HttpMethod.Get, "https://example.com/it's");

        var result = CurlBuilder.Build(request);

        result.Should().Contain("'https://example.com/it'\\''s'");
    }
}
