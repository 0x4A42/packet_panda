using FluentAssertions;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.Http;

public class HttpMethodTests
{
    [Fact]
    public void Enum_HasExactlyTheFourV1Methods()
    {
        Enum.GetNames<HttpMethod>()
            .Should()
            .BeEquivalentTo("Get", "Post", "Put", "Delete");
    }
}
