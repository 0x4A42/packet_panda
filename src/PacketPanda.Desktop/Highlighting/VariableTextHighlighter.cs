using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PacketPanda.Desktop.Highlighting;

/// <summary>
/// Updates a TextBlock's Inlines so that <c>{{varname}}</c> patterns are colored:
/// blue if the variable resolves in the active environment, red if not, gray when
/// no environment is active. Plain text is rendered in the default body color.
/// </summary>
public static partial class VariableTextHighlighter
{
    private static readonly ImmutableSolidColorBrush DefaultBrush =
        new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly ImmutableSolidColorBrush ResolvedBrush =
        new(Color.FromRgb(0x20, 0x7A, 0xC1));
    private static readonly ImmutableSolidColorBrush UnresolvedBrush =
        new(Color.FromRgb(0xCC, 0x33, 0x33));
    private static readonly ImmutableSolidColorBrush NoEnvBrush =
        new(Color.FromRgb(0xAA, 0xAA, 0xAA));

    [GeneratedRegex(@"\{\{([^}]+)\}\}")]
    private static partial Regex VarPattern();

    public static void Apply(
        TextBlock target,
        string? text,
        IReadOnlyDictionary<string, string>? variables)
    {
        target.Inlines ??= new InlineCollection();
        target.Inlines.Clear();

        if (string.IsNullOrEmpty(text)) return;

        var hasEnv = variables is not null;
        var lastEnd = 0;

        foreach (Match match in VarPattern().Matches(text))
        {
            if (match.Index > lastEnd)
            {
                target.Inlines.Add(new Run(text[lastEnd..match.Index])
                {
                    Foreground = DefaultBrush,
                });
            }

            var varName = match.Groups[1].Value.Trim();
            IBrush brush;
            if (!hasEnv) brush = NoEnvBrush;
            else if (variables!.ContainsKey(varName)) brush = ResolvedBrush;
            else brush = UnresolvedBrush;

            target.Inlines.Add(new Run(match.Value) { Foreground = brush });
            lastEnd = match.Index + match.Length;
        }

        if (lastEnd < text.Length)
        {
            target.Inlines.Add(new Run(text[lastEnd..]) { Foreground = DefaultBrush });
        }
    }
}
