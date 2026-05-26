using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Services;

public interface IDialogService
{
    Task<bool> ConfirmDiscardAsync(string tabTitle);
    Task<bool> ConfirmAsync(string title, string message);
    Task<string?> PromptTextAsync(string title, string prompt);
    Task<string?> ShowImportCurlAsync();
    Task AlertAsync(string title, string message);
    Task ShowSettingsAsync(SettingsViewModel vm);
}
