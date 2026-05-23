using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PacketPanda.Desktop.Services;

public sealed class AvaloniaFilePickerService(Window window) : IFilePickerService
{
    private static readonly FilePickerFileType JsonCollection =
        new("JSON collection") { Patterns = ["*.json"] };
    private static readonly FilePickerFileType JsonEnvironment =
        new("JSON environment") { Patterns = ["*.json"] };

    public async Task<string?> PickOpenAsync()
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Environment",
            AllowMultiple = false,
            FileTypeFilter = [JsonEnvironment]
        });
        return files.Count == 1 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickSaveEnvironmentAsync(string? suggestedName = null)
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Environment",
            DefaultExtension = "json",
            SuggestedFileName = suggestedName,
            FileTypeChoices = [JsonEnvironment]
        });
        return file?.Path.LocalPath;
    }

    public async Task<IReadOnlyList<string>> PickOpenMultipleAsync()
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Collection(s)",
            AllowMultiple = true,
            FileTypeFilter = [JsonCollection]
        });
        return files.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> PickOpenFolderAsync()
    {
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false
        });
        return folders.Count == 1 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> PickSaveCollectionAsync()
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "New Collection",
            DefaultExtension = "json",
            FileTypeChoices = [JsonCollection]
        });
        return file?.Path.LocalPath;
    }

    public async Task<string?> PickSingleFileAsync()
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { AllowMultiple = false });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
