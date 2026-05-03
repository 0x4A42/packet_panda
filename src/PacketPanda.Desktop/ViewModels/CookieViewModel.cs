namespace PacketPanda.Desktop.ViewModels;

public sealed class CookieViewModel
{
    public CookieViewModel(string name, string value, string raw)
    {
        Name = name;
        Value = value;
        Raw = raw;
    }

    public string Name { get; }
    public string Value { get; }
    public string Raw { get; }

    public static CookieViewModel FromHeader(string headerValue)
    {
        var first = headerValue.Split(';')[0].Trim();
        var eq = first.IndexOf('=');
        var name = eq >= 0 ? first[..eq].Trim() : first;
        var value = eq >= 0 ? first[(eq + 1)..].Trim() : string.Empty;
        return new CookieViewModel(name, value, headerValue);
    }
}
