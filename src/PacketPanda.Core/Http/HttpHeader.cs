namespace PacketPanda.Core.Http;

public sealed record HttpHeader(string Name, string Value, bool Enabled = true);
