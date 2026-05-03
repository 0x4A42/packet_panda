using FluentAssertions;
using PacketPanda.Core.Scripting;

namespace PacketPanda.Tests.Scripting;

public sealed class ScriptSessionTests
{
    [Fact]
    public void PpExpect_Equal_PassesWhenValuesMatch()
    {
        var expect = new PpExpect(200.0);
        var act = () => expect.to.equal(200.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void PpExpect_Equal_ThrowsWhenValuesDiffer()
    {
        var expect = new PpExpect(200.0);
        var act = () => expect.to.equal(404.0);
        act.Should().Throw<Exception>().WithMessage("*Expected*");
    }

    [Fact]
    public void PpExpect_Above_PassesWhenActualIsHigher()
    {
        var expect = new PpExpect(201.0);
        var act = () => expect.to.be.above(200.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void PpExpect_Above_ThrowsWhenActualIsLower()
    {
        var expect = new PpExpect(100.0);
        var act = () => expect.to.be.above(200.0);
        act.Should().Throw<Exception>().WithMessage("*above*");
    }

    [Fact]
    public void PpExpect_Include_PassesWhenStringContainsSubstring()
    {
        var expect = new PpExpect("hello world");
        var act = () => expect.to.include("world");
        act.Should().NotThrow();
    }

    [Fact]
    public void PpExpect_Include_ThrowsWhenStringDoesNotContain()
    {
        var expect = new PpExpect("hello world");
        var act = () => expect.to.include("xyz");
        act.Should().Throw<Exception>().WithMessage("*include*");
    }
}
