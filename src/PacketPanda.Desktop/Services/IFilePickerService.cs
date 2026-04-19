namespace PacketPanda.Desktop.Services;

public interface IFilePickerService
{
    Task<string?> PickOpenAsync();
    Task<string?> PickSaveAsync();
}
