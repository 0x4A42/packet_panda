namespace PacketPanda.Desktop.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync();
}
