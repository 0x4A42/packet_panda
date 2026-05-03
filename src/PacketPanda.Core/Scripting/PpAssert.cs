#pragma warning disable IDE1006 // lowercase names are intentional: JS-facing API
namespace PacketPanda.Core.Scripting;

public sealed class PpExpect
{
    private readonly object? _value;
    public PpExpect(object? value) { _value = value; }
    public PpExpectTo to => new(_value);
}

public sealed class PpExpectTo
{
    private readonly object? _value;
    public PpExpectTo(object? value) { _value = value; }

    // .be returns self so .to.be.above(n) chains correctly
    public PpExpectTo be => this;

    public void equal(object? expected)
    {
        if (!Equals(_value, expected))
            throw new Exception($"Expected {Format(expected)} but got {Format(_value)}");
    }

    public void above(double n)
    {
        double actual;
        try { actual = Convert.ToDouble(_value); }
        catch { throw new Exception($"Expected a number but got {Format(_value)}"); }
        if (actual <= n)
            throw new Exception($"Expected {actual} to be above {n}");
    }

    public void include(string substring)
    {
        var actual = _value?.ToString() ?? string.Empty;
        if (!actual.Contains(substring, StringComparison.Ordinal))
            throw new Exception($"Expected '{actual}' to include '{substring}'");
    }

    private static string Format(object? value) => value?.ToString() ?? "null";
}
