using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using PacketPanda.Core.Environments;
using PacketPanda.Core.History;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using PacketPanda.Desktop.Views;

namespace PacketPanda.Desktop;

// ReSharper disable once PartialTypeWithSinglePart
public partial class App : Application
{
    private HttpRequestSender? _sender;
    private IResourceDictionary? _activeThemeDictionary;

    public static App Instance => Current as App ?? throw new InvalidOperationException("App instance not initialized");

    public void ApplyTheme(bool isDark)
    {
        var uri = isDark
            ? new Uri("avares://PacketPanda/Assets/Themes/Dark.axaml")
            : new Uri("avares://PacketPanda/Assets/Themes/Light.axaml");

        var merged = Resources.MergedDictionaries;
        if (_activeThemeDictionary is not null)
            merged.Remove(_activeThemeDictionary);

        _activeThemeDictionary = AvaloniaXamlLoader.Load(uri) as IResourceDictionary
            ?? throw new InvalidOperationException($"Theme file did not produce a ResourceDictionary: {uri}");
        merged.Insert(0, _activeThemeDictionary);

        RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var startupSettings = JsonSettingsStore.LoadOrDefault();

            ApplyTheme(startupSettings.IsDarkTheme);

            var sender = HttpRequestSender.Create(startupSettings);
            _sender = sender;
            var collectionStore = new JsonCollectionFileStore();
            var settingsStore = new JsonSettingsStore(JsonSettingsStore.DefaultPath);
            var environmentStore = new JsonEnvironmentFileStore();

            var mainWindow = new MainWindow();

            var filePicker = new AvaloniaFilePickerService(mainWindow);
            var dialog = new AvaloniaDialogService(mainWindow);

            var avaloniaClipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;
            IClipboardService? clipboardService = avaloniaClipboard is not null
                ? new AvaloniaClipboardService(avaloniaClipboard)
                : null;

            var sidebar = new CollectionSidebarViewModel(collectionStore, settingsStore, filePicker, dialog);

            // ReSharper disable AccessToModifiedClosure
            ShellViewModel? shell = null;
            var historyStore = new JsonHistoryStore(JsonHistoryStore.DefaultPath, startupSettings.HistoryLimit);
            RequestEditorViewModel EditorFactory() => new(
                sender,
                collectionStore,
                new VariableResolver(),
                // ReSharper disable once NullableWarningSuppressionIsUsed
                shell!.ActiveEnvironmentObservable,
                // ReSharper disable once NullableWarningSuppressionIsUsed
                updateVariable: (name, value, target) => shell.UpdateActiveEnvironmentVariableAsync(name, value, target),
                // ReSharper disable once NullableWarningSuppressionIsUsed
                openEnvironmentEditor: () => shell.SetEnvironmentsTabCommand.Execute().Subscribe(),
                historyStore: historyStore,
                clipboardService: clipboardService,
                filePickerService: filePicker);

            var variableResolver = new VariableResolver();
            shell = new ShellViewModel(sidebar, EditorFactory, dialog, environmentStore, filePicker, settingsStore, historyStore,
                runnerFactory: folder => new CollectionRunnerViewModel(
                    folder,
                    sender,
                    variableResolver,
                    // ReSharper disable once NullableWarningSuppressionIsUsed
                    shell!.ActiveEnvironmentObservable,
                    // ReSharper disable once NullableWarningSuppressionIsUsed
                    updateVariable: (name, value, target) => shell.UpdateActiveEnvironmentVariableAsync(name, value, target),
                    // ReSharper disable once NullableWarningSuppressionIsUsed
                    openRequest: node => shell.OpenRequestNodeInTabAsync(node)));
            // ReSharper restore AccessToModifiedClosure
            mainWindow.DataContext = shell;

            desktop.MainWindow = mainWindow;

            _ = InitializeShellAsync(settingsStore, shell);

            desktop.Exit += (_, _) => { _sender?.Dispose(); shell.Dispose(); };
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
