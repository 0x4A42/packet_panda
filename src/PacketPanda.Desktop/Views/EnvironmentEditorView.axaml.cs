using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.ReactiveUI;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class EnvironmentEditorView : ReactiveUserControl<EnvironmentEditorViewModel>
{
    private TextBlock? _nameMirror;

    public EnvironmentEditorView()
    {
        InitializeComponent();

        _nameMirror = new TextBlock
        {
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 15,
            IsVisible = false
        };

        NameBox.AttachedToVisualTree += (_, _) =>
        {
            if (_nameMirror is not null && NameBox.Parent is Panel panel &&
                !panel.Children.Contains(_nameMirror))
                panel.Children.Add(_nameMirror);
            MeasureNameBox();
        };

        NameBox.TextChanged += (_, _) => MeasureNameBox();

        NameBox.LostFocus += (_, _) => ViewModel?.SaveCommand.Execute().Subscribe();

        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ViewModel?.SaveCommand.Execute().Subscribe();
                NameBox.ClearSelection();
            }
        };
    }

    private void MeasureNameBox()
    {
        if (_nameMirror is null) return;
        _nameMirror.Text = string.IsNullOrEmpty(NameBox.Text) ? " " : NameBox.Text;
        _nameMirror.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = _nameMirror.DesiredSize.Width;
        var padding = NameBox.Padding.Left + NameBox.Padding.Right + 4;
        NameBox.Width = Math.Max(NameBox.MinWidth, textWidth + padding);
    }
}
