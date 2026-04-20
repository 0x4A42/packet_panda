using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using PacketPanda.Desktop.Views;

namespace PacketPanda.Desktop;

public partial class App : Application
{
    private HttpClient? _httpClient;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _httpClient = new HttpClient();
            var sender = new HttpRequestSender(_httpClient);
            var store = new TomlRequestFileStore();
            var scanner = new WorkspaceScanner();
            var settingsStore = new TomlSettingsStore(TomlSettingsStore.DefaultPath);

            var mainWindow = new MainWindow();

            var filePicker = new AvaloniaFilePickerService(mainWindow);
            var folderPicker = new AvaloniaFolderPickerService(mainWindow);
            var dialog = new AvaloniaDialogService(mainWindow);

            var sidebar = new CollectionSidebarViewModel(folderPicker, scanner, settingsStore);

            RequestEditorViewModel EditorFactory() => new(sender, store, filePicker);

            var shell = new ShellViewModel(sidebar, EditorFactory, dialog, store, filePicker);
            mainWindow.DataContext = shell;

            desktop.MainWindow = mainWindow;

            _ = AutoReopenAsync(sidebar, settingsStore);

            desktop.Exit += (_, _) => { _httpClient?.Dispose(); shell.Dispose(); };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task AutoReopenAsync(CollectionSidebarViewModel sidebar, ISettingsStore settings)
    {
        try
        {
            var app = await settings.LoadAsync();
            if (!string.IsNullOrEmpty(app.LastWorkspacePath) && Directory.Exists(app.LastWorkspacePath))
                await sidebar.LoadWorkspaceAsync(app.LastWorkspacePath);
        }
        catch (SettingsException)
        {
            // Non-fatal — sidebar starts empty.
        }
    }
}
