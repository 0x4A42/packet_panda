using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PacketPanda.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnNumericValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (sender is NumericUpDown nud && nud.Value is null)
            nud.Value = nud.Minimum;
    }
}
