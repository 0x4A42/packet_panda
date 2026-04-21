using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace PacketPanda.Desktop.Highlighting;

public sealed partial class VariableColorizer : DocumentColorizingTransformer
{
    private static readonly SolidColorBrush ResolvedBrush =
        new(Color.FromRgb(0x20, 0x7A, 0xC1));   // blue
    private static readonly SolidColorBrush UnresolvedBrush =
        new(Color.FromRgb(0xCC, 0x33, 0x33));    // red
    private static readonly SolidColorBrush NeutralBrush =
        new(Color.FromRgb(0xE5, 0x9A, 0x00));    // amber (no active env)

    private HashSet<string>? _resolvedNames;

    [GeneratedRegex(@"\{\{([^}]+)\}\}")]
    private static partial Regex Pattern();

    public void UpdateEnvironment(IReadOnlyDictionary<string, string>? vars)
    {
        _resolvedNames = vars is null
            ? null
            : new HashSet<string>(vars.Keys, StringComparer.Ordinal);
    }

    protected override void ColorizeLine(AvaloniaEdit.Document.DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        foreach (Match m in Pattern().Matches(text))
        {
            var varName = m.Groups[1].Value.Trim();
            var brush = _resolvedNames is null
                ? NeutralBrush
                : (_resolvedNames.Contains(varName) ? ResolvedBrush : UnresolvedBrush);

            ChangeLinePart(
                line.Offset + m.Index,
                line.Offset + m.Index + m.Length,
                el => el.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
