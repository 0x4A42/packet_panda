using FluentAssertions;
using PacketPanda.Core.Http;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.Http;

public sealed class CurlParserTests
{
    [Fact]
    public void Parse_SimpleGet_ReturnsGetRequest()
    {
        var result = CurlParser.Parse("curl https://example.com/api");

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://example.com/api");
        result.Method.Should().Be(HttpMethod.Get);
        result.Headers.Should().BeEmpty();
        result.Body.Should().BeOfType<NoBody>();
    }

    [Fact]
    public void Parse_ExplicitGetMethod_ReturnsGetRequest()
    {
        var result = CurlParser.Parse("curl -X GET https://example.com/api");

        result!.Method.Should().Be(HttpMethod.Get);
        result.Url.Should().Be("https://example.com/api");
    }

    [Fact]
    public void Parse_PostWithJsonBody_ReturnsPostWithJsonBody()
    {
        var result = CurlParser.Parse(
            "curl -X POST https://example.com/api -H 'Content-Type: application/json' -d '{\"key\":\"value\"}'");

        result.Should().NotBeNull();
        result!.Method.Should().Be(HttpMethod.Post);
        result.Body.Should().BeOfType<JsonBody>();
        ((JsonBody)result.Body).Text.Should().Be("{\"key\":\"value\"}");
    }

    [Fact]
    public void Parse_BodyWithoutMethod_DefaultsToPost()
    {
        var result = CurlParser.Parse("curl https://example.com -d 'some data'");

        result!.Method.Should().Be(HttpMethod.Post);
        result.Body.Should().BeOfType<PlainTextBody>();
    }

    [Fact]
    public void Parse_MultipleHeaders_AllParsed()
    {
        var result = CurlParser.Parse(
            "curl https://example.com -H 'Accept: application/json' -H 'X-Api-Key: abc123'");

        result!.Headers.Should().HaveCount(2);
        result.Headers[0].Name.Should().Be("Accept");
        result.Headers[0].Value.Should().Be("application/json");
        result.Headers[1].Name.Should().Be("X-Api-Key");
        result.Headers[1].Value.Should().Be("abc123");
    }

    [Fact]
    public void Parse_FormUrlencodedContentType_ReturnsFormBody()
    {
        var result = CurlParser.Parse(
            "curl -X POST https://example.com -H 'Content-Type: application/x-www-form-urlencoded' -d 'name=Alice&age=30'");

        result!.Body.Should().BeOfType<FormBody>();
        var form = (FormBody)result.Body;
        form.Fields.Should().HaveCount(2);
        form.Fields[0].Name.Should().Be("name");
        form.Fields[0].Value.Should().Be("Alice");
        form.Fields[1].Name.Should().Be("age");
        form.Fields[1].Value.Should().Be("30");
    }

    [Fact]
    public void Parse_DataRawFlag_ParsesBody()
    {
        var result = CurlParser.Parse("curl https://example.com --data-raw 'hello world'");

        result!.Body.Should().BeOfType<PlainTextBody>();
        ((PlainTextBody)result.Body).Text.Should().Be("hello world");
    }

    [Fact]
    public void Parse_UrlFlag_ExtractsUrl()
    {
        var result = CurlParser.Parse("curl --url https://example.com/path");

        result!.Url.Should().Be("https://example.com/path");
    }

    [Fact]
    public void Parse_QuotedUrl_StripsQuotes()
    {
        var result = CurlParser.Parse("curl 'https://example.com/path?q=1'");

        result!.Url.Should().Be("https://example.com/path?q=1");
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsNull()
    {
        CurlParser.Parse(null!).Should().BeNull();
        CurlParser.Parse(string.Empty).Should().BeNull();
        CurlParser.Parse("   ").Should().BeNull();
    }

    [Fact]
    public void Parse_NotStartingWithCurl_ReturnsNull()
    {
        CurlParser.Parse("wget https://example.com").Should().BeNull();
    }

    [Fact]
    public void Parse_NoUrl_ReturnsNull()
    {
        CurlParser.Parse("curl -X POST").Should().BeNull();
    }
}
