using System.Reactive;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using ReactiveUI;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Desktop.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IHttpRequestSender _sender;
    private readonly IRequestFileStore _store;
    private readonly IFilePickerService _picker;

    private HttpMethod _selectedMethod = HttpMethod.Get;
    private string _url = string.Empty;
    private string? _requestBody;
    private int? _statusCode;
    private long? _elapsedMilliseconds;
    private string? _responseBody;
    private string? _errorMessage;
    private string? _currentFilePath;

    public MainWindowViewModel(IHttpRequestSender sender, IRequestFileStore store, IFilePickerService picker)
    {
        _sender = sender;
        _store = store;
        _picker = picker;

        var canSend = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));
        var canSave = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));

        SendCommand = ReactiveCommand.CreateFromTask(SendAsync, canSend);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync, canSave);
    }

    public HttpMethod SelectedMethod
    {
        get => _selectedMethod;
        set => this.RaiseAndSetIfChanged(ref _selectedMethod, value);
    }

    public string Url
    {
        get => _url;
        set => this.RaiseAndSetIfChanged(ref _url, value);
    }

    public string? RequestBody
    {
        get => _requestBody;
        set => this.RaiseAndSetIfChanged(ref _requestBody, value);
    }

    public int? StatusCode
    {
        get => _statusCode;
        set => this.RaiseAndSetIfChanged(ref _statusCode, value);
    }

    public long? ElapsedMilliseconds
    {
        get => _elapsedMilliseconds;
        set => this.RaiseAndSetIfChanged(ref _elapsedMilliseconds, value);
    }

    public string? ResponseBody
    {
        get => _responseBody;
        set => this.RaiseAndSetIfChanged(ref _responseBody, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        set => this.RaiseAndSetIfChanged(ref _currentFilePath, value);
    }

    public ReactiveCommand<Unit, Unit> SendCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }

    public HttpMethod[] AvailableMethods { get; } =
    {
        HttpMethod.Get,
        HttpMethod.Post,
        HttpMethod.Put,
        HttpMethod.Delete
    };

    private async Task SendAsync()
    {
        ErrorMessage = null;

        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        var request = new HttpRequest(SelectedMethod, Url, body);
        var result = await _sender.SendAsync(request);

        switch (result)
        {
            case SendResult.Ok ok:
                StatusCode = ok.Response.StatusCode;
                ResponseBody = ok.Response.Body;
                ElapsedMilliseconds = ok.Response.ElapsedMilliseconds;
                ErrorMessage = null;
                break;

            case SendResult.Error err:
                ErrorMessage = err.Message;
                StatusCode = null;
                ResponseBody = null;
                ElapsedMilliseconds = null;
                break;
        }
    }

    private async Task OpenAsync()
    {
        var path = await _picker.PickOpenAsync();
        if (path is null) return;

        try
        {
            var request = await _store.LoadAsync(path);
            SelectedMethod = request.Method;
            Url = request.Url;
            RequestBody = request.Body;
            StatusCode = null;
            ResponseBody = null;
            ElapsedMilliseconds = null;
            ErrorMessage = null;
            CurrentFilePath = path;
        }
        catch (RequestFileException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task SaveAsync()
    {
        if (CurrentFilePath is null)
        {
            await SaveAsAsync();
            return;
        }

        await SaveToPathAsync(CurrentFilePath);
    }

    private async Task SaveAsAsync()
    {
        var path = await _picker.PickSaveAsync();
        if (path is null) return;

        var succeeded = await SaveToPathAsync(path);
        if (succeeded)
            CurrentFilePath = path;
    }

    private async Task<bool> SaveToPathAsync(string path)
    {
        try
        {
            var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
            var request = new HttpRequest(SelectedMethod, Url, body);
            await _store.SaveAsync(path, request);
            ErrorMessage = null;
            return true;
        }
        catch (RequestFileException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }
}
