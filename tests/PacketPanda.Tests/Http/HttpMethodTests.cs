using FluentAssertions;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.Http;

public class HttpMethodTests
{
    [Fact]
    public void Enum_has_exactly_the_four_v1_methods()
    {
        Enum.GetNames<HttpMethod>()
            .Should()
            .BeEquivalentTo("Get", "Post", "Put", "Delete");
    }
}
