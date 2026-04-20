namespace PacketPanda.Desktop.Services;

public interface IDialogService
{
    Task<bool> ConfirmDiscardAsync(string tabTitle);
}
