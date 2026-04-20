using System.Text;
using System.Text.RegularExpressions;

namespace PacketPanda.Core.Environments;

public sealed partial class VariableResolver : IVariableResolver
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex Pattern();

    public ResolveResult Resolve(string template, Environment? activeEnv)
    {
        if (string.IsNullOrEmpty(template))
            return new ResolveResult(template ?? string.Empty, Array.Empty<string>());

        var sb = new StringBuilder(template.Length);
        var unresolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var last = 0;

        foreach (Match m in Pattern().Matches(template))
        {
            sb.Append(template, last, m.Index - last);
            var name = m.Groups[1].Value;

            if (activeEnv is not null && activeEnv.Variables.TryGetValue(name, out var value))
            {
                sb.Append(value);
            }
            else
            {
                sb.Append(m.Value);
                if (seen.Add(name))
                    unresolved.Add(name);
            }

            last = m.Index + m.Length;
        }
        sb.Append(template, last, template.Length - last);

        return new ResolveResult(sb.ToString(), unresolved);
    }
}
