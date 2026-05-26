using FluentAssertions;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Tests.ViewModels;

public sealed class CookieViewModelTests
{
    [Fact]
    public void FromHeader_SimpleNameValue_ParsesCorrectly()
    {
        var vm = CookieViewModel.FromHeader("session=abc123; Path=/; HttpOnly");

        vm.Name.Should().Be("session");
        vm.Value.Should().Be("abc123");
        vm.Raw.Should().Be("session=abc123; Path=/; HttpOnly");
    }

    [Fact]
    public void FromHeader_EqualsSignInValue_UsesFirstEqualsAsDelimiter()
    {
        var vm = CookieViewModel.FromHeader("token=a=b=c; Path=/");

        vm.Name.Should().Be("token");
        vm.Value.Should().Be("a=b=c");
    }

    [Fact]
    public void FromHeader_EmptyValue_ReturnsEmptyString()
    {
        var vm = CookieViewModel.FromHeader("name=; Path=/");

        vm.Name.Should().Be("name");
        vm.Value.Should().BeEmpty();
    }

    [Fact]
    public void FromHeader_NoEqualsSign_TreatsWholePairAsNameWithEmptyValue()
    {
        var vm = CookieViewModel.FromHeader("just-a-name; Path=/");

        vm.Name.Should().Be("just-a-name");
        vm.Value.Should().BeEmpty();
    }

    [Fact]
    public void FromHeader_NoAttributes_ParsesNameAndValue()
    {
        var vm = CookieViewModel.FromHeader("key=value");

        vm.Name.Should().Be("key");
        vm.Value.Should().Be("value");
    }
}
