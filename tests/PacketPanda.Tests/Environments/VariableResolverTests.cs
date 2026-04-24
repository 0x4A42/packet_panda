using FluentAssertions;
using PacketPanda.Core.Environments;
using Xunit;
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Tests.Environments;

public class VariableResolverTests
{
    private static Environment Env(params (string k, string v)[] pairs) =>
        new("e", "/tmp/e.toml", pairs.ToDictionary(p => p.k, p => p.v));

    [Fact]
    public void Resolve_NoReferences_ReturnsTemplateUnchanged()
    {
        var r = new VariableResolver().Resolve("https://api/users", Env());
        r.Text.Should().Be("https://api/users");
        r.UnresolvedNames.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_KnownReference_Substitutes()
    {
        var r = new VariableResolver().Resolve("{{baseUrl}}/users", Env(("baseUrl", "https://api")));
        r.Text.Should().Be("https://api/users");
        r.UnresolvedNames.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_WhitespaceAroundName_Substitutes()
    {
        var r = new VariableResolver().Resolve("{{  baseUrl  }}/x", Env(("baseUrl", "A")));
        r.Text.Should().Be("A/x");
    }

    [Fact]
    public void Resolve_UnknownReference_LeavesLiteralAndReportsName()
    {
        var r = new VariableResolver().Resolve("{{foo}}/{{bar}}", Env(("foo", "F")));
        r.Text.Should().Be("F/{{bar}}");
        r.UnresolvedNames.Should().ContainSingle().Which.Should().Be("bar");
    }

    [Fact]
    public void Resolve_DuplicateUnresolved_DedupedPreservingFirstOrder()
    {
        var r = new VariableResolver().Resolve("{{a}}-{{b}}-{{a}}", Env());
        r.UnresolvedNames.Should().Equal("a", "b");
    }

    [Fact]
    public void Resolve_NullEnvironment_AllMatchesUnresolved()
    {
        var r = new VariableResolver().Resolve("{{x}}", null);
        r.Text.Should().Be("{{x}}");
        r.UnresolvedNames.Should().Equal("x");
    }

    [Fact]
    public void Resolve_InvalidSyntax_LeavesBracesUntouchedAndNotCountedUnresolved()
    {
        var r = new VariableResolver().Resolve("{{123}} {{ }} {{-x}}", Env());
        r.Text.Should().Be("{{123}} {{ }} {{-x}}");
        r.UnresolvedNames.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_CaseSensitive_DoesNotMatchDifferentCase()
    {
        var r = new VariableResolver().Resolve("{{Foo}}", Env(("foo", "lower")));
        r.UnresolvedNames.Should().Equal("Foo");
    }
}
