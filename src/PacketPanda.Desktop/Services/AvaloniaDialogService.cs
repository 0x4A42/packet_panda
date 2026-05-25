using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PacketPanda.Desktop.ViewModels;
using PacketPanda.Desktop.Views;

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
        var cancel = new Button { Content = "Cancel", Margin = new Avalonia.Thickness(4), IsDefault = true, IsCancel = true };

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

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(16, 16, 16, 8)
        };

        var yes = new Button { Content = "Yes", Margin = new Avalonia.Thickness(4) };
        var no = new Button { Content = "No", Margin = new Avalonia.Thickness(4), IsDefault = true, IsCancel = true };

        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => { result = false; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(8)
        };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);

        var layout = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(messageBlock);

        dialog.Content = layout;

        await dialog.ShowDialog(owner);
        return result;
    }

    public async Task AlertAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(16, 16, 16, 8)
        };

        var ok = new Button { Content = "OK", Margin = new Avalonia.Thickness(4), IsDefault = true };
        ok.Click += (_, _) => dialog.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(8)
        };
        buttons.Children.Add(ok);

        var layout = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(messageBlock);

        dialog.Content = layout;
        await dialog.ShowDialog(owner);
    }

    public async Task<string?> PromptTextAsync(string title, string prompt)
    {
        var inputWindow = new Window
        {
            Title = title,
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var textBox = new TextBox
        {
            Margin = new Avalonia.Thickness(12, 12, 12, 8),
            Watermark = prompt
        };

        var okButton = new Button { Content = "OK", Margin = new Avalonia.Thickness(4) };
        var cancelButton = new Button { Content = "Cancel", Margin = new Avalonia.Thickness(4), IsCancel = true };

        okButton.Click += (_, _) => { inputWindow.Close(); };
        cancelButton.Click += (_, _) => { textBox.Text = null; inputWindow.Close(); };
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) inputWindow.Close();
            if (e.Key == Avalonia.Input.Key.Escape) { textBox.Text = null; inputWindow.Close(); }
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(8)
        };
        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(okButton);

        var layout = new DockPanel();
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        layout.Children.Add(buttonRow);
        layout.Children.Add(textBox);

        inputWindow.Content = layout;

        await inputWindow.ShowDialog(owner);

        var result = textBox.Text;
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }

    public async Task ShowSettingsAsync(SettingsViewModel vm)
    {
        var view = new SettingsView { DataContext = vm };
        var dialog = new Window
        {
            Title = "Settings",
            Content = view,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
        };
        using var _ = vm.SaveCommand.Subscribe(_ => dialog.Close());
        await dialog.ShowDialog(owner);
    }

    public async Task<string?> PromptLargeTextAsync(string title, string placeholder)
    {
        var textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Watermark = placeholder,
            MinHeight = 140,
            FontFamily = new FontFamily("Consolas,Menlo,monospace"),
            FontSize = 12,
        };

        var okButton = new Button { Content = "Import", IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true };
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { okButton, cancelButton }
        };

        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Avalonia.Thickness(16),
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                textBox,
                buttonRow
            }
        };

        var dialog = new Window
        {
            Title = title,
            Content = panel,
            Width = 540,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SizeToContent = SizeToContent.Height,
        };

        string? result = null;
        okButton.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
