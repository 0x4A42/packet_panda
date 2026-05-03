namespace PacketPanda.Core.Http;

public sealed record QueryParam(string Key, string Value, bool Enabled = true);
