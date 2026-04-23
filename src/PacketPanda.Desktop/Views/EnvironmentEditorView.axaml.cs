using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class EnvironmentEditorView : ReactiveUserControl<EnvironmentEditorViewModel>
{
    private bool _suppressSaveOnLostFocus;

    public EnvironmentEditorView()
    {
        InitializeComponent();

        NameBox.TextChanged += (_, _) => MeasureNameBox();
        NameBox.AttachedToVisualTree += (_, _) => MeasureNameBox();

        NameBox.LostFocus += (_, _) =>
        {
            if (_suppressSaveOnLostFocus) { _suppressSaveOnLostFocus = false; return; }
            ViewModel?.SaveCommand.Execute().Subscribe();
        };

        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _suppressSaveOnLostFocus = true;
                ViewModel?.SaveCommand.Execute().Subscribe();
                ClearFocus();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _suppressSaveOnLostFocus = true;
                ViewModel?.RevertName();
                ClearFocus();
            }
        };

        // Defer BeginEdit so cell selection has completed before edit mode starts
        VarsGrid.CellPointerPressed += (_, _) =>
            Dispatcher.UIThread.Post(() => VarsGrid.BeginEdit());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TopLevel.GetTopLevel(this)?.AddHandler(
            PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TopLevel.GetTopLevel(this)?.RemoveHandler(
            PointerPressedEvent, OnWindowPointerPressed);
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // If the click target is not inside the DataGrid, commit any active edit
        var source = e.Source as Visual;
        if (source is not null && !VarsGrid.IsVisualAncestorOf(source) && source != VarsGrid)
            VarsGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        // If the click target is not a focusable input inside this view, clear focus
        if (source is not null && !NameBox.IsVisualAncestorOf(source) && source != NameBox
            && !VarsGrid.IsVisualAncestorOf(source) && source != VarsGrid)
            ClearFocus();
    }

    private void ClearFocus()
    {
        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
    }

    private void MeasureNameBox()
    {
        var text = string.IsNullOrEmpty(NameBox.Text) ? " " : NameBox.Text;
        var typeface = new Typeface(NameBox.FontFamily, NameBox.FontStyle, NameBox.FontWeight);
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, NameBox.FontSize, Brushes.Black);
        var padding = NameBox.Padding.Left + NameBox.Padding.Right + 4;
        NameBox.Width = Math.Max(NameBox.MinWidth, ft.Width + padding);
    }
}
