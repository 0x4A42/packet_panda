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

    [Fact]
    public void XmlBody_RoundTripSerialized_RestoresConcreteType()
    {
        BodyConfig original = new XmlBody("<root/>");

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<BodyConfig>(json, Options);

        restored.Should().BeOfType<XmlBody>().Which.Text.Should().Be("<root/>");
    }

    [Fact]
    public void MultipartBody_RoundTripSerialized_RestoresPartsWithTypes()
    {
        BodyConfig original = new MultipartBody([
            new MultipartPart("field1", MultipartPartType.Text, "hello", true),
            new MultipartPart("file1", MultipartPartType.File, "/tmp/a.png", false),
        ]);

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<BodyConfig>(json, Options);

        var body = restored.Should().BeOfType<MultipartBody>().Subject;
        body.Parts.Should().HaveCount(2);
        body.Parts[0].Should().Be(new MultipartPart("field1", MultipartPartType.Text, "hello", true));
        body.Parts[1].Should().Be(new MultipartPart("file1", MultipartPartType.File, "/tmp/a.png", false));
    }
}
