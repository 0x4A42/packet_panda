using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PacketPanda.Desktop.Services;

public sealed class AvaloniaFolderPickerService(Window window) : IFolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Workspace Folder",
            AllowMultiple = false
        });

        return folders.Count == 1 ? folders[0].Path.LocalPath : null;
    }
}
