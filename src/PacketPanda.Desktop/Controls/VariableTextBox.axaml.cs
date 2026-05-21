using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using PacketPanda.Desktop.Highlighting;

namespace PacketPanda.Desktop.Controls;

/// <summary>
/// A single-line text box that renders <c>{{variable}}</c> spans colored by source
/// (active env / globals / unresolved), matching the URL bar. The real glyphs are
/// transparent; a TextBlock overlay paints the colored text.
/// </summary>
public partial class VariableTextBox : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<VariableTextBox, string?>(
            nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<VariableTextBox, string?>(nameof(Watermark));

    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> GlobalsVariablesProperty =
        AvaloniaProperty.Register<VariableTextBox, IReadOnlyDictionary<string, string>?>(
            nameof(GlobalsVariables));

    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> ActiveEnvVariablesProperty =
        AvaloniaProperty.Register<VariableTextBox, IReadOnlyDictionary<string, string>?>(
            nameof(ActiveEnvVariables));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public IReadOnlyDictionary<string, string>? GlobalsVariables
    {
        get => GetValue(GlobalsVariablesProperty);
        set => SetValue(GlobalsVariablesProperty, value);
    }

    public IReadOnlyDictionary<string, string>? ActiveEnvVariables
    {
        get => GetValue(ActiveEnvVariablesProperty);
        set => SetValue(ActiveEnvVariablesProperty, value);
    }

    public VariableTextBox()
    {
        InitializeComponent();

        Editor.TextChanged += (_, _) =>
        {
            if (Text != Editor.Text)
                Text = Editor.Text;
            Refresh();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            if (Editor.Text != Text)
                Editor.Text = Text;
            Refresh();
        }
        else if (change.Property == WatermarkProperty)
        {
            Editor.Watermark = Watermark;
        }
        else if (change.Property == GlobalsVariablesProperty ||
                 change.Property == ActiveEnvVariablesProperty)
        {
            Refresh();
        }
    }

    private void Refresh() =>
        VariableTextHighlighter.Apply(Overlay, Text, GlobalsVariables, ActiveEnvVariables);
}
