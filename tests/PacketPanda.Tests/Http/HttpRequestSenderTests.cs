using System.Net.Http;
using FluentAssertions;
using PacketPanda.Core.Http;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.Http;

public class HttpRequestSenderTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly HttpRequestSender _sender;

    public HttpRequestSenderTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient();
        _sender = new HttpRequestSender(_httpClient);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        _httpClient.Dispose();
    }

    [Fact]
    public async Task Get_Returning200_YieldsOkWithStatusBodyAndTiming()
    {
        _server
            .Given(Request.Create().WithPath("/hello").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("hi"));

        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, _server.Url + "/hello"));

        result.Should().BeOfType<SendResult.Ok>();
        var ok = (SendResult.Ok)result;
        ok.Response.StatusCode.Should().Be(200);
        ok.Response.Body.Should().Be("hi");
        ok.Response.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Non2xxResponse_IsReturnedAsOkNotError()
    {
        _server
            .Given(Request.Create().WithPath("/missing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404).WithBody("nope"));

        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, _server.Url + "/missing"));

        result.Should().BeOfType<SendResult.Ok>();
        var ok = (SendResult.Ok)result;
        ok.Response.StatusCode.Should().Be(404);
        ok.Response.Body.Should().Be("nope");
    }

    [Fact]
    public async Task UnreachableHost_YieldsError()
    {
        // Port 1 is almost always closed; connection attempt will fail fast.
        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, "http://127.0.0.1:1/"));

        result.Should().BeOfType<SendResult.Error>();
        ((SendResult.Error)result).Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Post_WithBody_SendsJsonContentTypeAndBody()
    {
        _server
            .Given(Request.Create().WithPath("/echo").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("created"));

        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Post, _server.Url + "/echo", "{\"a\":1}"));

        result.Should().BeOfType<SendResult.Ok>();
        var received = _server.LogEntries.Single();
        received.RequestMessage!.Body!.Should().Be("{\"a\":1}");
        received.RequestMessage!.Headers!["Content-Type"].Single()
            .Should().StartWith("application/json");
    }

    [Fact]
    public async Task MalformedUrl_YieldsErrorNotException()
    {
        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, "not a url"));

        result.Should().BeOfType<SendResult.Error>();
    }

    [Fact]
    public async Task SendAsync_EnabledHeaders_AreSentWithRequest()
    {
        _server.Given(Request.Create().WithPath("/h").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200));

        await _sender.SendAsync(new HttpRequest(HttpMethod.Get, _server.Url + "/h",
            Headers: [new HttpHeader("X-Custom", "hello"), new HttpHeader("X-Skip", "no", Enabled: false)]));

        var sentHeaders = _server.LogEntries.Single().RequestMessage!.Headers!;
        sentHeaders.Should().ContainKey("X-Custom");
        sentHeaders.Should().NotContainKey("X-Skip");
    }

    [Fact]
    public async Task SendAsync_BearerAuth_InjectsAuthorizationHeader()
    {
        _server.Given(Request.Create().WithPath("/h").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200));

        await _sender.SendAsync(new HttpRequest(HttpMethod.Get, _server.Url + "/h",
            Auth: new BearerAuth("my-token")));

        var sentHeaders = _server.LogEntries.Single().RequestMessage!.Headers!;
        sentHeaders["Authorization"].Should().ContainSingle("Bearer my-token");
    }

    [Fact]
    public async Task SendAsync_BasicAuth_InjectsBase64EncodedHeader()
    {
        _server.Given(Request.Create().WithPath("/h").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200));

        await _sender.SendAsync(new HttpRequest(HttpMethod.Get, _server.Url + "/h",
            Auth: new BasicAuth("user", "pass")));

        var expected = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:pass"));
        var sentHeaders = _server.LogEntries.Single().RequestMessage!.Headers!;
        sentHeaders["Authorization"].Should().ContainSingle(expected);
    }

    [Fact]
    public async Task SendAsync_ApiKeyAuth_HeaderTarget_InjectsNamedHeader()
    {
        _server.Given(Request.Create().WithPath("/h").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200));

        await _sender.SendAsync(new HttpRequest(HttpMethod.Get, _server.Url + "/h",
            Auth: new ApiKeyAuth("X-API-Key", "secret", ApiKeyTarget.Header)));

        var sentHeaders = _server.LogEntries.Single().RequestMessage!.Headers!;
        sentHeaders["X-API-Key"].Should().ContainSingle("secret");
    }

    [Fact]
    public async Task SendAsync_ApiKeyAuth_QueryParamTarget_AppendsToUrl()
    {
        _server.Given(Request.Create().WithPath("/h").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200));

        await _sender.SendAsync(new HttpRequest(HttpMethod.Get, _server.Url + "/h",
            Auth: new ApiKeyAuth("api_key", "secret", ApiKeyTarget.QueryParam)));

        var requestedUrl = _server.LogEntries.Single().RequestMessage!.Url;
        requestedUrl.Should().Contain("api_key=secret");
    }

    [Fact]
    public async Task SendAsync_ApiKeyAuth_QueryParamTarget_AppendsWithAmpersandWhenQueryStringExists()
    {
        _server.Given(Request.Create().WithPath("/h").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200));

        await _sender.SendAsync(new HttpRequest(HttpMethod.Get, _server.Url + "/h?existing=1",
            Auth: new ApiKeyAuth("api_key", "secret", ApiKeyTarget.QueryParam)));

        var requestedUrl = _server.LogEntries.Single().RequestMessage!.Url;
        requestedUrl.Should().Contain("existing=1");
        requestedUrl.Should().Contain("api_key=secret");
    }

    [Fact]
    public async Task SendAsync_NoAuth_DoesNotInjectAuthorizationHeader()
    {
        _server.Given(Request.Create().WithPath("/h").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200));

        await _sender.SendAsync(new HttpRequest(HttpMethod.Get, _server.Url + "/h", Auth: new NoAuth()));

        var sentHeaders = _server.LogEntries.Single().RequestMessage!.Headers!;
        sentHeaders.Should().NotContainKey("Authorization");
    }
}
