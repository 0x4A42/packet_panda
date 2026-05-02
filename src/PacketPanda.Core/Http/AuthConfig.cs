namespace PacketPanda.Core.Http;

public abstract record AuthConfig;
public sealed record NoAuth() : AuthConfig;
public sealed record ApiKeyAuth(string Name, string Value, ApiKeyTarget Target) : AuthConfig;
public sealed record BearerAuth(string Token) : AuthConfig;
public sealed record BasicAuth(string Username, string Password) : AuthConfig;

public enum ApiKeyTarget { Header, QueryParam }
