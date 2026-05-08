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
            var collectionStore = new JsonCollectionFileStore();
            var settingsStore = new JsonSettingsStore(JsonSettingsStore.DefaultPath);
            var environmentStore = new JsonEnvironmentFileStore();

            var mainWindow = new MainWindow();

            var filePicker = new AvaloniaFilePickerService(mainWindow);
            var dialog = new AvaloniaDialogService(mainWindow);

            var sidebar = new CollectionSidebarViewModel(collectionStore, settingsStore, filePicker, dialog);

            ShellViewModel? shell = null;
            RequestEditorViewModel EditorFactory() => new(
                sender,
                collectionStore,
                new VariableResolver(),
                shell?.ActiveEnvironmentObservable ?? System.Reactive.Linq.Observable.Return<EnvironmentContext?>(null),
                updateVariable: (name, value, target) => shell!.UpdateActiveEnvironmentVariableAsync(name, value, target),
                openEnvironmentEditor: () => shell!.SetEnvironmentsTabCommand.Execute().Subscribe());

            var variableResolver = new VariableResolver();
            shell = new ShellViewModel(sidebar, EditorFactory, dialog, environmentStore, filePicker, settingsStore,
                runnerFactory: folder => new CollectionRunnerViewModel(
                    folder,
                    sender,
                    variableResolver,
                    shell?.ActiveEnvironmentObservable ?? System.Reactive.Linq.Observable.Return<EnvironmentContext?>(null),
                    updateVariable: (name, value, target) => shell!.UpdateActiveEnvironmentVariableAsync(name, value, target)));
            mainWindow.DataContext = shell;

            desktop.MainWindow = mainWindow;

            _ = InitializeShellAsync(settingsStore, shell);

            desktop.Exit += (_, _) => { _httpClient?.Dispose(); shell.Dispose(); };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeShellAsync(ISettingsStore settings, ShellViewModel shell)
    {
        try
        {
            var app = await settings.LoadAsync();
            await shell.InitializeAsync(app);
        }
        catch (SettingsException)
        {
            // Non-fatal — shell starts with defaults.
        }
    }
}
