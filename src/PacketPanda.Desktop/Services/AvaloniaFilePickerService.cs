using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PacketPanda.Desktop.Services;

public sealed class AvaloniaFilePickerService(Window window) : IFilePickerService
{
    public async Task<string?> PickOpenAsync()
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Request",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("TOML request") { Patterns = ["*.toml"] }]
        });

        return files.Count == 1 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickSaveAsync()
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Request",
            DefaultExtension = "toml",
            FileTypeChoices = [new FilePickerFileType("TOML request") { Patterns = ["*.toml"] }]
        });

        return file?.Path.LocalPath;
    }
}
