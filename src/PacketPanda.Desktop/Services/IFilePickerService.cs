namespace PacketPanda.Desktop.Services;

public interface IFilePickerService
{
    Task<string?> PickOpenAsync();
    Task<string?> PickSaveEnvironmentAsync();
    Task<IReadOnlyList<string>> PickOpenMultipleAsync();
    Task<string?> PickOpenFolderAsync();
    Task<string?> PickSaveCollectionAsync();
}
