using FluentAssertions;
using PacketPanda.Core;

namespace PacketPanda.Tests;

public class CoreSmokeTests
{
    [Fact]
    public void Version_IsNotEmpty()
    {
        PacketPandaVersion.Value.Should().NotBeNullOrWhiteSpace();
    }
}
