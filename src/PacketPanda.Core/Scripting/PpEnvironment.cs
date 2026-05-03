#pragma warning disable IDE1006
using Environment = PacketPanda.Core.Environments.Environment;

namespace PacketPanda.Core.Scripting;

public sealed class PpEnvironment
{
    private readonly Environment? _activeEnv;
    private readonly Dictionary<string, string> _overlay;

    public PpEnvironment(Environment? activeEnv, Dictionary<string, string> overlay)
    {
        _activeEnv = activeEnv;
        _overlay = overlay;
    }

    public string? get(string key)
    {
        if (_overlay.TryGetValue(key, out var val)) return val;
        return _activeEnv?.Variables.TryGetValue(key, out val) == true ? val : null;
    }

    public void set(string key, string value) => _overlay[key] = value;
}
