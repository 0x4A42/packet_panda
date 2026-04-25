using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;

namespace PacketPanda.Desktop.Highlighting;

public sealed partial class VariableColorizer : DocumentColorizingTransformer
{
    private static readonly ImmutableSolidColorBrush BlueBrush =
        new(Color.FromRgb(0x20, 0x7A, 0xC1));   // resolved
    private static readonly ImmutableSolidColorBrush RedBrush =
        new(Color.FromRgb(0xCC, 0x33, 0x33));    // unresolved
    private static readonly ImmutableSolidColorBrush GrayBrush =
        new(Color.FromRgb(0xAA, 0xAA, 0xAA));   // no active env

    private HashSet<string>? _knownKeys;
    private bool _hasActiveEnv;

    [GeneratedRegex(@"\{\{([^}]+)\}\}")]
    private static partial Regex VarPattern();

    public void UpdateEnvironment(IReadOnlyDictionary<string, string>? variables)
    {
        _hasActiveEnv = variables is not null;
        _knownKeys = variables is not null
            ? new HashSet<string>(variables.Keys, StringComparer.Ordinal)
            : null;
    }

    protected override void ColorizeLine(AvaloniaEdit.Document.DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        foreach (Match match in VarPattern().Matches(text))
        {
            var varName = match.Groups[1].Value.Trim();
            IBrush brush;
            if (!_hasActiveEnv)
                brush = GrayBrush;
            else if (_knownKeys!.Contains(varName))
                brush = BlueBrush;
            else
                brush = RedBrush;

            ChangeLinePart(
                line.Offset + match.Index,
                line.Offset + match.Index + match.Length,
                el => el.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
