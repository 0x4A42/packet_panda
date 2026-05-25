using FluentAssertions;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.Http;

// ReSharper disable NullableWarningSuppressionIsUsed
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
            new HttpRequest(HttpMethod.Post, _server.Url + "/echo", new JsonBody("{\"a\":1}")));

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
    public async Task SendAsync_BearerAuth_InjectsAuthorisationHeader()
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
    public async Task SendAsync_NoAuth_DoesNotInjectAuthorisationHeader()
    {
        _server.Given(Request.Create().WithPath("/h").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200));

        await _sender.SendAsync(new HttpRequest(HttpMethod.Get, _server.Url + "/h", Auth: new NoAuth()));

        var sentHeaders = _server.LogEntries.Single().RequestMessage!.Headers!;
        sentHeaders.Should().NotContainKey("Authorization");
    }

    [Fact]
    public async Task Send_EnabledQueryParams_AppendedToUrl()
    {
        _server.Given(Request.Create().WithPath("/search").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200).WithBody(""));

        var request = new HttpRequest(HttpMethod.Get, _server.Url + "/search",
            QueryParams: [new QueryParam("q", "hello"), new QueryParam("page", "2")]);

        await _sender.SendAsync(request);

        var requestedUrl = _server.LogEntries.Single().RequestMessage!.Url;
        requestedUrl.Should().Contain("q=hello");
        requestedUrl.Should().Contain("page=2");
    }

    [Fact]
    public async Task Send_DisabledQueryParams_NotAppendedToUrl()
    {
        _server.Given(Request.Create().WithPath("/search").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200).WithBody(""));

        var request = new HttpRequest(HttpMethod.Get, _server.Url + "/search",
            QueryParams: [new QueryParam("debug", "1", Enabled: false)]);

        await _sender.SendAsync(request);

        var requestedUrl = _server.LogEntries.Single().RequestMessage!.Url;
        requestedUrl.Should().NotContain("debug");
    }

    [Fact]
    public async Task Send_PopulatesResponseHeaders()
    {
        _server.Given(Request.Create().WithPath("/h").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("X-Request-Id", "abc123")
                   .WithBody(""));

        var result = await _sender.SendAsync(new HttpRequest(HttpMethod.Get, _server.Url + "/h"));

        var ok = result.Should().BeOfType<SendResult.Ok>().Subject;
        ok.Response.Headers.Should().NotBeNull();
        ok.Response.Headers!.Should().Contain(h =>
            h.Name.Equals("X-Request-Id", StringComparison.OrdinalIgnoreCase) && h.Value == "abc123");
    }

    [Fact]
    public async Task Patch_SendsWithPatchMethod()
    {
        _server
            .Given(Request.Create().WithPath("/item/1").UsingMethod("PATCH"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("patched"));

        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Patch, _server.Url + "/item/1"));

        result.Should().BeOfType<SendResult.Ok>()
            .Which.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Head_SendsWithHeadMethod_ReturnsOk()
    {
        _server
            .Given(Request.Create().WithPath("/check").UsingMethod("HEAD"))
            .RespondWith(Response.Create().WithStatusCode(200));

        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Head, _server.Url + "/check"));

        result.Should().BeOfType<SendResult.Ok>()
            .Which.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Options_SendsWithOptionsMethod()
    {
        _server
            .Given(Request.Create().WithPath("/resource").UsingMethod("OPTIONS"))
            .RespondWith(Response.Create().WithStatusCode(204));

        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Options, _server.Url + "/resource"));

        result.Should().BeOfType<SendResult.Ok>()
            .Which.Response.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task Post_WithJsonBody_SetsJsonContentType()
    {
        _server
            .Given(Request.Create().WithPath("/json-ct").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await _sender.SendAsync(new HttpRequest(
            HttpMethod.Post, _server.Url + "/json-ct",
            Body: new JsonBody("{\"x\":1}")));

        var received = _server.LogEntries.Single();
        received.RequestMessage!.Headers!["Content-Type"].Single()
            .Should().StartWith("application/json");
    }

    [Fact]
    public async Task Post_WithFormBody_SetsFormUrlEncodedContentType()
    {
        _server
            .Given(Request.Create().WithPath("/form-ct").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        var fields = new List<FormField> { new("key", "value", true) };
        await _sender.SendAsync(new HttpRequest(
            HttpMethod.Post, _server.Url + "/form-ct",
            Body: new FormBody(fields)));

        var received = _server.LogEntries.Single();
        received.RequestMessage!.Headers!["Content-Type"].Single()
            .Should().StartWith("application/x-www-form-urlencoded");
    }

    [Fact]
    public async Task Post_WithNoBody_SendsNoContent()
    {
        _server
            .Given(Request.Create().WithPath("/nobody").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        var result = await _sender.SendAsync(new HttpRequest(
            HttpMethod.Post, _server.Url + "/nobody",
            Body: new NoBody()));

        result.Should().BeOfType<SendResult.Ok>();
    }

    [Fact]
    public async Task Send_MultipartBodyWithTextPart_SendsMultipartFormData()
    {
        _server
            .Given(Request.Create().WithPath("/upload").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("uploaded"));

        var request = new HttpRequest(
            HttpMethod.Post,
            _server.Url + "/upload",
            new MultipartBody([
                new MultipartPart("field1", MultipartPartType.Text, "hello", true),
                new MultipartPart("disabled", MultipartPartType.Text, "skip", false),
            ]));

        var result = await _sender.SendAsync(request);

        result.Should().BeOfType<SendResult.Ok>();
        _server.LogEntries.Should().ContainSingle(e =>
            e.RequestMessage!.Path == "/upload" &&
            e.RequestMessage!.Headers!.ContainsKey("Content-Type") &&
            e.RequestMessage!.Headers!["Content-Type"].Any(v => v.Contains("multipart/form-data")));
    }

    [Fact]
    public void MimeTypeFor_KnownExtensions_ReturnsCorrectType()
    {
        HttpRequestSender.MimeTypeFor("image.png").Should().Be("image/png");
        HttpRequestSender.MimeTypeFor("data.json").Should().Be("application/json");
        HttpRequestSender.MimeTypeFor("doc.pdf").Should().Be("application/pdf");
        HttpRequestSender.MimeTypeFor("file.unknown").Should().Be("application/octet-stream");
        HttpRequestSender.MimeTypeFor("noextension").Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task Create_DefaultSettings_CanSendRequest()
    {
        _server
            .Given(Request.Create().WithPath("/create-default").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        var settings = new AppSettings();
        using var sender = HttpRequestSender.Create(settings);
        var result = await sender.SendAsync(
            new HttpRequest(HttpMethod.Get, _server.Url + "/create-default"));

        result.Should().BeOfType<SendResult.Ok>();
    }

    [Fact]
    public async Task Create_SslVerificationFalse_CanSendRequest()
    {
        _server
            .Given(Request.Create().WithPath("/create-nossl").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        var settings = new AppSettings(SslVerification: false, TimeoutSeconds: 10);
        using var sender = HttpRequestSender.Create(settings);
        var result = await sender.SendAsync(
            new HttpRequest(HttpMethod.Get, _server.Url + "/create-nossl"));

        result.Should().BeOfType<SendResult.Ok>();
    }
}
