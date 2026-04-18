using FluentAssertions;
using PacketPanda.Core.Http;

namespace PacketPanda.Tests.Http;

public class HttpMethodTests
{
    [Fact]
    public void Enum_has_exactly_the_four_v1_methods()
    {
        Enum.GetNames<global::PacketPanda.Core.Http.HttpMethod>()
            .Should()
            .BeEquivalentTo("Get", "Post", "Put", "Delete");
    }
}
