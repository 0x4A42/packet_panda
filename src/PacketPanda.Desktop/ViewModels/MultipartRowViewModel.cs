using System.Reactive;
using PacketPanda.Core.Http;
using PacketPanda.Desktop.Services;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public sealed class MultipartRowViewModel : ViewModelBase
{
    private bool _enabled = true;
    private string _name = string.Empty;
    private MultipartPartType _partType = MultipartPartType.Text;
    private string _value = string.Empty;
    private string _filePath = string.Empty;

    private readonly IFilePickerService? _filePicker;

    public MultipartRowViewModel(Action<MultipartRowViewModel> onRemove, IFilePickerService? filePicker = null)
    {
        _filePicker = filePicker;
        RemoveCommand = ReactiveCommand.Create(() => onRemove(this));
        BrowseCommand = ReactiveCommand.CreateFromTask(BrowseAsync);
    }

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> BrowseCommand { get; }

    public bool Enabled
    {
        get => _enabled;
        set => this.RaiseAndSetIfChanged(ref _enabled, value);
    }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public MultipartPartType PartType
    {
        get => _partType;
        set
        {
            this.RaiseAndSetIfChanged(ref _partType, value);
            this.RaisePropertyChanged(nameof(IsTextPart));
            this.RaisePropertyChanged(nameof(IsFilePart));
        }
    }

    public string Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    public string FilePath
    {
        get => _filePath;
        set => this.RaiseAndSetIfChanged(ref _filePath, value);
    }

    public bool IsTextPart => PartType == MultipartPartType.Text;
    public bool IsFilePart => PartType == MultipartPartType.File;

    public MultipartPartType[] AvailablePartTypes { get; } =
        [MultipartPartType.Text, MultipartPartType.File];

    private async Task BrowseAsync()
    {
        if (_filePicker is null) return;
        var path = await _filePicker.PickSingleFileAsync();
        if (path is not null)
            FilePath = path;
    }
}
