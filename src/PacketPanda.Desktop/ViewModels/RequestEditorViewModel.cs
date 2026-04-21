using System.Reactive;
using AvaloniaEdit.Document;
using PacketPanda.Core.Environments;
using Environment = PacketPanda.Core.Environments.Environment;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using ReactiveUI;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Desktop.ViewModels;

public class RequestEditorViewModel : ViewModelBase, IDisposable, ITabContent
{
    private readonly IHttpRequestSender _sender;
    private readonly IRequestFileStore _store;
    private readonly IFilePickerService _picker;
    private readonly IVariableResolver _resolver;
    private Environment? _activeEnv;
    private IDisposable? _activeEnvSub;
    private bool _syncingDocuments;

    private HttpMethod _selectedMethod = HttpMethod.Get;
    private string _url = string.Empty;
    private string? _requestBody;
    private int? _statusCode;
    private long? _elapsedMilliseconds;
    private string? _responseBody;
    private string? _errorMessage;
    private string? _currentFilePath;
    private bool _isDirty;
    private RequestSnapshot? _savedSnapshot;

    private sealed record RequestSnapshot(HttpMethod Method, string Url, string? Body);

    public RequestEditorViewModel(
        IHttpRequestSender sender,
        IRequestFileStore store,
        IFilePickerService picker,
        IVariableResolver resolver,
        IObservable<Environment?> activeEnvironment)
    {
        _sender = sender;
        _store = store;
        _picker = picker;
        _resolver = resolver;
        _activeEnvSub = activeEnvironment.Subscribe(env =>
        {
            _activeEnv = env;
            this.RaisePropertyChanged(nameof(ActiveEnvironmentVariables));
        });

        UrlDocument = new TextDocument();
        BodyDocument = new TextDocument();

        UrlDocument.TextChanged += (_, _) =>
        {
            if (_syncingDocuments) return;
            _url = UrlDocument.Text;
            this.RaisePropertyChanged(nameof(Url));
            RecomputeDirty();
        };

        BodyDocument.TextChanged += (_, _) =>
        {
            if (_syncingDocuments) return;
            _requestBody = string.IsNullOrEmpty(BodyDocument.Text) ? null : BodyDocument.Text;
            this.RaisePropertyChanged(nameof(RequestBody));
            RecomputeDirty();
        };

        var canSend = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));
        var canSave = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));

        SendCommand = ReactiveCommand.CreateFromTask(SendAsync, canSend);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync, canSave);

        this.WhenAnyValue(vm => vm.SelectedMethod)
            .Subscribe(_ => RecomputeDirty());

        CaptureSnapshot();
    }

    public TextDocument UrlDocument { get; }
    public TextDocument BodyDocument { get; }

    public IReadOnlyDictionary<string, string>? ActiveEnvironmentVariables => _activeEnv?.Variables;

    public HttpMethod SelectedMethod
    {
        get => _selectedMethod;
        set => this.RaiseAndSetIfChanged(ref _selectedMethod, value);
    }

    public string Url
    {
        get => _url;
        set
        {
            if (_url == value) return;
            _url = value;
            this.RaisePropertyChanged(nameof(Url));
            SyncDocumentText(UrlDocument, value ?? string.Empty);
            RecomputeDirty();
        }
    }

    public string? RequestBody
    {
        get => _requestBody;
        set
        {
            if (_requestBody == value) return;
            _requestBody = value;
            this.RaisePropertyChanged(nameof(RequestBody));
            SyncDocumentText(BodyDocument, value ?? string.Empty);
            RecomputeDirty();
        }
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

    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
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

    private void SyncDocumentText(TextDocument doc, string text)
    {
        if (doc.Text == text) return;
        _syncingDocuments = true;
        try { doc.Text = text; }
        finally { _syncingDocuments = false; }
    }

    private async Task SendAsync()
    {
        ErrorMessage = null;

        var urlResult = _resolver.Resolve(Url, _activeEnv);
        var bodyTemplate = RequestBody ?? string.Empty;
        var bodyResult = _resolver.Resolve(bodyTemplate, _activeEnv);

        var unresolved = urlResult.UnresolvedNames
            .Concat(bodyResult.UnresolvedNames)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unresolved.Count > 0)
        {
            ErrorMessage = $"Unresolved variables: {string.Join(", ", unresolved)}";
            StatusCode = null;
            ResponseBody = null;
            ElapsedMilliseconds = null;
            return;
        }

        var body = string.IsNullOrEmpty(bodyResult.Text) ? null : bodyResult.Text;
        var request = new HttpRequest(SelectedMethod, urlResult.Text, body);
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

    public void Dispose()
    {
        _activeEnvSub?.Dispose();
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
            CaptureSnapshot();
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
            CaptureSnapshot();
            ErrorMessage = null;
            return true;
        }
        catch (RequestFileException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    private void RecomputeDirty()
    {
        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        var current = new RequestSnapshot(SelectedMethod, Url, body);
        IsDirty = _savedSnapshot != current;
    }

    private void CaptureSnapshot()
    {
        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        _savedSnapshot = new RequestSnapshot(SelectedMethod, Url, body);
        IsDirty = false;
    }

    public void MarkSaved()
    {
        CaptureSnapshot();
    }

    // ITabContent
    string ITabContent.DisplayTitle => CurrentFilePath is null
        ? "Untitled"
        : Path.GetFileNameWithoutExtension(CurrentFilePath);

    string? ITabContent.MethodLabel => SelectedMethod.ToString().ToUpperInvariant();
}
