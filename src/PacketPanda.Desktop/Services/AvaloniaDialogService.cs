using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PacketPanda.Desktop.Services;

public sealed class AvaloniaDialogService(Window owner) : IDialogService
{
    public async Task<bool> ConfirmDiscardAsync(string tabTitle)
    {
        var result = false;
        var dialog = new Window
        {
            Title = "Unsaved changes",
            Width = 360,
            Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var message = new TextBlock
        {
            Text = $"'{tabTitle}' has unsaved changes. Discard?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(16, 16, 16, 8)
        };

        var discard = new Button { Content = "Discard", Margin = new Avalonia.Thickness(4) };
        var cancel = new Button { Content = "Cancel", Margin = new Avalonia.Thickness(4), IsDefault = true };

        discard.Click += (_, _) => { result = true; dialog.Close(); };
        cancel.Click += (_, _) => { result = false; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(8)
        };
        buttons.Children.Add(discard);
        buttons.Children.Add(cancel);

        var layout = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(message);

        dialog.Content = layout;

        await dialog.ShowDialog(owner);
        return result;
    }
}
