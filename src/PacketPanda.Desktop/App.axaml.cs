using System.Net.Http;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
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
    private IResourceDictionary? _activeThemeDictionary;

    public static App Instance => (App)Current!;

    public void ApplyTheme(bool isDark)
    {
        var uri = isDark
            ? new Uri("avares://PacketPanda/Assets/Themes/Dark.axaml")
            : new Uri("avares://PacketPanda/Assets/Themes/Light.axaml");

        var merged = Resources.MergedDictionaries;
        if (_activeThemeDictionary != null)
            merged.Remove(_activeThemeDictionary);

        _activeThemeDictionary = AvaloniaXamlLoader.Load(uri) as IResourceDictionary
            ?? throw new InvalidOperationException($"Theme file did not produce a ResourceDictionary: {uri}");
        merged.Insert(0, _activeThemeDictionary);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ApplyTheme(isDark: false);

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
                    updateVariable: (name, value, target) => shell!.UpdateActiveEnvironmentVariableAsync(name, value, target),
                    openRequest: node => shell!.OpenRequestNodeInTabAsync(node)));
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
