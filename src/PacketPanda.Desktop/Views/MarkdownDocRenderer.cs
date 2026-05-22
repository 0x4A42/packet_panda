using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PacketPanda.Desktop.Views;

internal static class MarkdownDocRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UsePipeTables().Build();

    public static Control Render(string markdown)
    {
        var doc = Markdig.Markdown.Parse(markdown, Pipeline);
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        foreach (var block in doc)
            AddBlock(block, panel.Children);
        return new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private static void AddBlock(Block block, Avalonia.Controls.Controls children)
    {
        switch (block)
        {
            case HeadingBlock h:
                children.Add(RenderHeading(h));
                break;
            case ParagraphBlock p:
                children.Add(RenderParagraph(p));
                break;
            case FencedCodeBlock fc:
                children.Add(RenderCodeBlock(fc.Lines.ToString()));
                break;
            case CodeBlock c:
                children.Add(RenderCodeBlock(c.Lines.ToString()));
                break;
            case ListBlock list:
                children.Add(RenderList(list));
                break;
            case Table table:
                children.Add(RenderTable(table));
                break;
            case ThematicBreakBlock:
                children.Add(new Separator());
                break;
            case ContainerBlock container:
                foreach (var child in container)
                    AddBlock(child, children);
                break;
        }
    }

    private static TextBlock RenderHeading(HeadingBlock h)
    {
        double fontSize = h.Level switch { 1 => 26, 2 => 20, 3 => 16, 4 => 14, _ => 13 };
        var tb = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, h.Level <= 2 ? 12 : 6, 0, 2)
        };
        BuildInlines(h.Inline, tb.Inlines);
        return tb;
    }

    private static SelectableTextBlock RenderParagraph(ParagraphBlock p)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        BuildInlines(p.Inline, tb.Inlines);
        return tb;
    }

    private static Border RenderCodeBlock(string code)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 127, 127, 127)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8),
            Child = new SelectableTextBlock
            {
                Text = code.TrimEnd(),
                FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New,monospace"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private static StackPanel RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 3, Margin = new Thickness(8, 0, 0, 0) };
        int num = 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var bulletTb = new TextBlock
            {
                Text = list.IsOrdered ? $"{num++}. " : "• ",
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(bulletTb, 0);

            var content = new StackPanel { Spacing = 4 };
            Grid.SetColumn(content, 1);
            foreach (var child in item)
                AddBlock(child, content.Children);

            row.Children.Add(bulletTb);
            row.Children.Add(content);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static Border RenderTable(Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        if (rows.Count == 0) return new Border();

        int colCount = rows.Max(r => r.Count);
        var grid = new Grid();
        for (int i = 0; i < colCount; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int ri = 0; ri < rows.Count; ri++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var row = rows[ri];
            for (int ci = 0; ci < row.Count; ci++)
            {
                var cell = (TableCell)row[ci];
                var tb = new SelectableTextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 4),
                    FontWeight = row.IsHeader ? FontWeight.SemiBold : FontWeight.Normal
                };
                foreach (var b in cell)
                    if (b is ParagraphBlock pb)
                        BuildInlines(pb.Inline, tb.Inlines);

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = row.IsHeader
                        ? new SolidColorBrush(Color.FromArgb(50, 127, 127, 127))
                        : null,
                    Child = tb
                };
                Grid.SetRow(border, ri);
                Grid.SetColumn(border, ci);
                grid.Children.Add(border);
            }
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1, 1, 0, 0),
            Child = grid,
            Margin = new Thickness(0, 4)
        };
    }

    private static void BuildInlines(ContainerInline? container, InlineCollection? inlines)
    {
        if (container is null || inlines is null) return;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    inlines.Add(new Run { Text = lit.Content.ToString() });
                    break;
                case CodeInline code:
                    inlines.Add(new Run
                    {
                        Text = code.Content,
                        FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New,monospace"),
                        FontSize = 12,
                        Background = new SolidColorBrush(Color.FromArgb(40, 127, 127, 127))
                    });
                    break;
                case EmphasisInline em:
                    if (em.DelimiterCount == 2)
                    {
                        var bold = new Bold();
                        BuildInlines(em, bold.Inlines);
                        inlines.Add(bold);
                    }
                    else
                    {
                        var italic = new Italic();
                        BuildInlines(em, italic.Inlines);
                        inlines.Add(italic);
                    }
                    break;
                case LinkInline link:
                    BuildInlines(link, inlines);
                    break;
                case LineBreakInline:
                    inlines.Add(new LineBreak());
                    break;
                case ContainerInline nested:
                    BuildInlines(nested, inlines);
                    break;
            }
        }
    }
}
