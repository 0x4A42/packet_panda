using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PacketPanda.Desktop.Highlighting;

/// <summary>
/// Populates a TextBlock's Inlines with colour-coded JSON: keys, string values,
/// numbers, and keywords each get a distinct brush. Non-token text inherits the
/// TextBlock's own foreground.
/// </summary>
public static partial class JsonHighlighter
{
    private static readonly ImmutableSolidColorBrush KeyBrush = new(Color.FromRgb(0x79, 0xB8, 0xFF));
    private static readonly ImmutableSolidColorBrush StringBrush = new(Color.FromRgb(0x9E, 0xCB, 0xFF));
    private static readonly ImmutableSolidColorBrush NumberBrush = new(Color.FromRgb(0xF7, 0x81, 0x66));
    private static readonly ImmutableSolidColorBrush KeywordBrush = new(Color.FromRgb(0xA3, 0x71, 0xF7));

    [GeneratedRegex("\"(?:[^\"\\\\]|\\\\.)*\"|-?\\d+\\.?\\d*(?:[eE][+-]?\\d+)?|\\b(?:true|false|null)\\b")]
    private static partial Regex TokenPattern();

    public static void Apply(TextBlock target, string? text)
    {
        target.Inlines ??= new InlineCollection();
        target.Inlines.Clear();
        if (string.IsNullOrEmpty(text)) return;

        var last = 0;
        foreach (Match m in TokenPattern().Matches(text))
        {
            if (m.Index > last)
                target.Inlines.Add(new Run(text[last..m.Index]));

            var tok = m.Value;
            IBrush brush;
            if (tok[0] == '"')
            {
                var j = m.Index + m.Length;
                while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                brush = j < text.Length && text[j] == ':' ? KeyBrush : StringBrush;
            }
            else if (tok is "true" or "false" or "null")
            {
                brush = KeywordBrush;
            }
            else
            {
                brush = NumberBrush;
            }

            target.Inlines.Add(new Run(tok) { Foreground = brush });
            last = m.Index + m.Length;
        }

        if (last < text.Length)
            target.Inlines.Add(new Run(text[last..]));
    }
}
