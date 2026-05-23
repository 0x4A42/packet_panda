using Avalonia.Input.Platform;

namespace PacketPanda.Desktop.Services;

public sealed class AvaloniaClipboardService : IClipboardService
{
    private readonly IClipboard _clipboard;

    public AvaloniaClipboardService(IClipboard clipboard)
    {
        _clipboard = clipboard;
    }

    public Task SetTextAsync(string text) => _clipboard.SetTextAsync(text) ?? Task.CompletedTask;
}
