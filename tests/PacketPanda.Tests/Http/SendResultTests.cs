using FluentAssertions;
using PacketPanda.Core.Http;

namespace PacketPanda.Tests.Http;

public class SendResultTests
{
    [Fact]
    public void Ok_WrapsAResponse()
    {
        var response = new HttpResponse(200, "hello", 42);
        var result = new SendResult.Ok(response);

        result.Response.Should().BeSameAs(response);
    }

    [Fact]
    public void PatternMatch_DistinguishesTheCases()
    {
        SendResult ok = new SendResult.Ok(new HttpResponse(200, "body", 1));
        SendResult err = new SendResult.Error("boom");

        Describe(ok).Should().Be("ok:200");
        Describe(err).Should().Be("err:boom");

        static string Describe(SendResult r) => r switch
        {
            SendResult.Ok o => $"ok:{o.Response.StatusCode}",
            SendResult.Error e => $"err:{e.Message}",
            _ => throw new InvalidOperationException()
        };
    }
}
