using System.Text.Json.Serialization;

namespace PacketPanda.Core.Http;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(NoAuth), "none")]
[JsonDerivedType(typeof(ApiKeyAuth), "apikey")]
[JsonDerivedType(typeof(BearerAuth), "bearer")]
[JsonDerivedType(typeof(BasicAuth), "basic")]
public abstract record AuthConfig;

public sealed record NoAuth() : AuthConfig;
public sealed record ApiKeyAuth(string Name, string Value, ApiKeyTarget Target) : AuthConfig;
public sealed record BearerAuth(string Token) : AuthConfig;
public sealed record BasicAuth(string Username, string Password) : AuthConfig;

public enum ApiKeyTarget { Header, QueryParam }
