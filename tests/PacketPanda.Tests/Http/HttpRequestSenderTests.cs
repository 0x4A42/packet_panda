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
    public async Task Get_returning_200_yields_Ok_with_status_body_and_timing()
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
    public async Task Non_2xx_response_is_returned_as_Ok_not_Error()
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
    public async Task Unreachable_host_yields_Error()
    {
        // Port 1 is almost always closed; connection attempt will fail fast.
        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, "http://127.0.0.1:1/"));

        result.Should().BeOfType<SendResult.Error>();
        ((SendResult.Error)result).Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Post_with_body_sends_json_content_type_and_body()
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
    public async Task Malformed_url_yields_Error_not_exception()
    {
        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, "not a url"));

        result.Should().BeOfType<SendResult.Error>();
    }
}
