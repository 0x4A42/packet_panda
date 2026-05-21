using System.Text.Json;
using FluentAssertions;
using PacketPanda.Core.Http;

namespace PacketPanda.Tests.History;

public sealed class PolymorphicSerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    [Fact]
    public void BodyConfig_RoundTrips_PreservingConcreteType()
    {
        BodyConfig original = new JsonBody("{\"a\":1}");

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<BodyConfig>(json, Options);

        restored.Should().BeOfType<JsonBody>();
        ((JsonBody)restored!).Text.Should().Be("{\"a\":1}");
    }

    [Fact]
    public void AuthConfig_RoundTrips_PreservingConcreteType()
    {
        AuthConfig original = new BearerAuth("secret-token");

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<AuthConfig>(json, Options);

        restored.Should().BeOfType<BearerAuth>();
        ((BearerAuth)restored!).Token.Should().Be("secret-token");
    }

    [Fact]
    public void FormBody_RoundTrips_PreservingFields()
    {
        BodyConfig original = new FormBody(new[] { new FormField("k", "v", true) });

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<BodyConfig>(json, Options);

        restored.Should().BeOfType<FormBody>();
        ((FormBody)restored!).Fields.Should().ContainSingle()
            .Which.Should().Be(new FormField("k", "v", true));
    }
}
