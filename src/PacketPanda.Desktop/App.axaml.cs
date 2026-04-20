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

            var mainWindow = new MainWindow();
            var picker = new AvaloniaFilePickerService(mainWindow);
            mainWindow.DataContext = new RequestEditorViewModel(sender, store, picker);

            desktop.MainWindow = mainWindow;

            desktop.Exit += (_, _) => _httpClient?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
