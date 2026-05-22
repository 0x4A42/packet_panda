using System.Text.Json.Serialization;

namespace PacketPanda.Core.Http;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(NoBody), "none")]
[JsonDerivedType(typeof(JsonBody), "json")]
[JsonDerivedType(typeof(PlainTextBody), "text")]
[JsonDerivedType(typeof(XmlBody), "xml")]
[JsonDerivedType(typeof(FormBody), "form")]
public abstract record BodyConfig;

public sealed record NoBody : BodyConfig;
public sealed record JsonBody(string Text) : BodyConfig;
public sealed record PlainTextBody(string Text) : BodyConfig;
public sealed record XmlBody(string Text) : BodyConfig;
public sealed record FormBody(IReadOnlyList<FormField> Fields) : BodyConfig;

public sealed record FormField(string Name, string Value, bool Enabled);
