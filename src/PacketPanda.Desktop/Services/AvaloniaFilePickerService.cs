using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PacketPanda.Desktop.Services;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    private readonly Window _window;

    public AvaloniaFilePickerService(Window window)
    {
        _window = window;
    }

    public async Task<string?> PickOpenAsync()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Request",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("TOML request") { Patterns = ["*.toml"] }]
        });

        return files.Count == 1 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickSaveAsync()
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Request",
            DefaultExtension = "toml",
            FileTypeChoices = [new FilePickerFileType("TOML request") { Patterns = ["*.toml"] }]
        });

        return file?.Path.LocalPath;
    }
}
