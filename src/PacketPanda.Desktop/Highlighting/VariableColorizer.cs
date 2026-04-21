using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace PacketPanda.Desktop.Highlighting;

public sealed partial class VariableColorizer : DocumentColorizingTransformer
{
    [GeneratedRegex(@"\{\{[^}]+\}\}")]
    private static partial Regex Pattern();

    protected override void ColorizeLine(AvaloniaEdit.Document.DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        foreach (Match m in Pattern().Matches(text))
        {
            ChangeLinePart(
                line.Offset + m.Index,
                line.Offset + m.Index + m.Length,
                el => el.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(0xE5, 0x9A, 0x00))));
        }
    }
}
