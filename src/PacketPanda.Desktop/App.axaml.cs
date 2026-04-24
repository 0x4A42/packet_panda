using System.Net.Http;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PacketPanda.Core.Environments;
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
            var dialog = new AvaloniaDialogService(mainWindow);

            var environmentStore = new TomlEnvironmentFileStore();

            var sidebar = new CollectionSidebarViewModel(scanner, settingsStore);

            ShellViewModel? shell = null;
            RequestEditorViewModel EditorFactory() => new(
                sender,
                store,
                filePicker,
                new VariableResolver(),
                shell?.ActiveEnvironmentObservable ?? System.Reactive.Linq.Observable.Return<PacketPanda.Core.Environments.Environment?>(null));

            shell = new ShellViewModel(sidebar, EditorFactory, dialog, store, filePicker, environmentStore, settingsStore);
            mainWindow.DataContext = shell;

            desktop.MainWindow = mainWindow;

            _ = AutoReopenAsync(sidebar, settingsStore, shell);

            desktop.Exit += (_, _) => { _httpClient?.Dispose(); shell.Dispose(); };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task AutoReopenAsync(CollectionSidebarViewModel sidebar, ISettingsStore settings, ShellViewModel shell)
    {
        try
        {
            var app = await settings.LoadAsync();
            // Set pending env name BEFORE loading workspace so RestoreSelectedEnvironment picks it up
            shell.SetPendingActiveEnvironmentName(app.ActiveEnvironmentName);
            if (!string.IsNullOrEmpty(app.LastWorkspacePath) && Directory.Exists(app.LastWorkspacePath))
                await sidebar.LoadWorkspaceAsync(app.LastWorkspacePath);
        }
        catch (SettingsException)
        {
            // Non-fatal — sidebar starts empty.
        }
    }
}
