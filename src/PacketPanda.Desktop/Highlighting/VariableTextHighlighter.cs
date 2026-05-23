using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PacketPanda.Desktop.Highlighting;

/// <summary>
/// Updates a TextBlock's Inlines so that <c>{{varname}}</c> patterns are colored by source:
/// green if resolved from the active environment, blue if resolved from globals only,
/// red if unresolved, gray when no environment is active at all.
/// </summary>
public static partial class VariableTextHighlighter
{
    private static readonly ImmutableSolidColorBrush ActiveEnvBrush =
        new(Color.FromRgb(0x27, 0xAE, 0x60)); // green — resolved from active environment
    private static readonly ImmutableSolidColorBrush GlobalsBrush =
        new(Color.FromRgb(0x20, 0x7A, 0xC1)); // blue — resolved from globals
    private static readonly ImmutableSolidColorBrush UnresolvedBrush =
        new(Color.FromRgb(0xCC, 0x33, 0x33));
    private static readonly ImmutableSolidColorBrush NoEnvBrush =
        new(Color.FromRgb(0xAA, 0xAA, 0xAA));

    [GeneratedRegex(@"\{\{([^}]+)\}\}")]
    private static partial Regex VarPattern();

    public static void Apply(
        TextBlock target,
        string? text,
        IReadOnlyDictionary<string, string>? globalsVars,
        IReadOnlyDictionary<string, string>? activeEnvVars)
    {
        target.Inlines ??= new InlineCollection();
        target.Inlines.Clear();

        if (string.IsNullOrEmpty(text)) return;

        var hasAnyEnv = globalsVars is not null || activeEnvVars is not null;
        var lastEnd = 0;

        foreach (Match match in VarPattern().Matches(text))
        {
            if (match.Index > lastEnd)
                target.Inlines.Add(new Run(text[lastEnd..match.Index]));

            var varName = match.Groups[1].Value.Trim();
            IBrush brush;
            if (!hasAnyEnv) brush = NoEnvBrush;
            else if (activeEnvVars?.ContainsKey(varName) == true) brush = ActiveEnvBrush;
            else if (globalsVars?.ContainsKey(varName) == true) brush = GlobalsBrush;
            else brush = UnresolvedBrush;

            target.Inlines.Add(new Run(match.Value) { Foreground = brush });
            lastEnd = match.Index + match.Length;
        }

        if (lastEnd < text.Length)
            target.Inlines.Add(new Run(text[lastEnd..]));
    }
}
