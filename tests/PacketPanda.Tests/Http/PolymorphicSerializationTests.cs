using System.Text.Json;
using FluentAssertions;
using PacketPanda.Core.Http;

namespace PacketPanda.Tests.Http;

public sealed class PolymorphicSerializationTests
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    [Fact]
    public void BodyConfig_RoundTripSerialized_RestoresConcreteType()
    {
        BodyConfig original = new JsonBody("{\"a\":1}");

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<BodyConfig>(json, Options);

        restored.Should().BeOfType<JsonBody>().Which.Text.Should().Be("{\"a\":1}");
    }

    [Fact]
    public void AuthConfig_RoundTripSerialized_RestoresConcreteType()
    {
        AuthConfig original = new BearerAuth("secret-token");

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<AuthConfig>(json, Options);

        restored.Should().BeOfType<BearerAuth>().Which.Token.Should().Be("secret-token");
    }

    [Fact]
    public void FormBody_RoundTripSerialized_RestoresAllFields()
    {
        BodyConfig original = new FormBody(new[] { new FormField("k", "v", true) });

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<BodyConfig>(json, Options);

        restored.Should().BeOfType<FormBody>().Which.Fields.Should().ContainSingle()
            .Which.Should().Be(new FormField("k", "v", true));
    }
}
