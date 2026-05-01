using System.Reactive;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using ReactiveUI;
using Environment = PacketPanda.Core.Environments.Environment;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Desktop.ViewModels;

public class RequestEditorViewModel : ViewModelBase, IDisposable, ITabContent
{
    private readonly IHttpRequestSender _sender;
    private readonly ICollectionFileStore _collectionStore;
    private readonly IVariableResolver _resolver;
    private Environment? _activeEnv;
    private IDisposable? _activeEnvSub;
    private IDisposable? _dirtySub;

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
    private string? _requestName;

    private sealed record RequestSnapshot(HttpMethod Method, string Url, string? Body);

    public RequestEditorViewModel(
        IHttpRequestSender sender,
        ICollectionFileStore collectionStore,
        IVariableResolver resolver,
        IObservable<Environment?> activeEnvironment)
    {
        _sender = sender;
        _collectionStore = collectionStore;
        _resolver = resolver;
        _activeEnvSub = activeEnvironment.Subscribe(env =>
        {
            _activeEnv = env;
            this.RaisePropertyChanged(nameof(ActiveEnvironmentVariables));
        });

        var canSend = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));
        var canSave = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));

        SendCommand = ReactiveCommand.CreateFromTask(SendAsync, canSend);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);

        _dirtySub = this.WhenAnyValue(vm => vm.SelectedMethod, vm => vm.Url, vm => vm.RequestBody)
            .Subscribe(_ => RecomputeDirty());

        CaptureSnapshot();
    }

    public string? RequestId { get; set; }

    public string? RequestName
    {
        get => _requestName;
        set => this.RaiseAndSetIfChanged(ref _requestName, value);
    }

    public string? CollectionFilePath { get; set; }

    public IReadOnlyDictionary<string, string>? ActiveEnvironmentVariables => _activeEnv?.Variables;

    public HttpMethod SelectedMethod
    {
        get => _selectedMethod;
        set => this.RaiseAndSetIfChanged(ref _selectedMethod, value);
    }

    public string Url
    {
        get => _url;
        set => this.RaiseAndSetIfChanged(ref _url, value ?? string.Empty);
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

    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    public ReactiveCommand<Unit, Unit> SendCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

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

        var urlResult = _resolver.Resolve(Url, _activeEnv);
        var bodyResult = _resolver.Resolve(RequestBody ?? string.Empty, _activeEnv);

        var unresolved = urlResult.UnresolvedNames
            .Concat(bodyResult.UnresolvedNames)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unresolved.Count > 0)
        {
            ErrorMessage = $"Unresolved variables: {string.Join(", ", unresolved)}";
            StatusCode = null; ResponseBody = null; ElapsedMilliseconds = null;
            return;
        }

        var body = string.IsNullOrEmpty(bodyResult.Text) ? null : bodyResult.Text;
        var result = await _sender.SendAsync(new HttpRequest(SelectedMethod, urlResult.Text, body));

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
                StatusCode = null; ResponseBody = null; ElapsedMilliseconds = null;
                break;
        }
    }

    private async Task SaveAsync()
    {
        if (RequestId is null || CollectionFilePath is null) return;

        try
        {
            var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
            await _collectionStore.UpdateRequestAsync(CollectionFilePath, RequestId, new HttpRequest(SelectedMethod, Url, body));
            CaptureSnapshot();
            ErrorMessage = null;
        }
        catch (RequestFileException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public void Dispose()
    {
        _activeEnvSub?.Dispose();
        _dirtySub?.Dispose();
    }

    private void RecomputeDirty()
    {
        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        IsDirty = _savedSnapshot != new RequestSnapshot(SelectedMethod, Url, body);
    }

    private void CaptureSnapshot()
    {
        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        _savedSnapshot = new RequestSnapshot(SelectedMethod, Url, body);
        IsDirty = false;
    }

    public void MarkSaved() => CaptureSnapshot();

    // ITabContent
    string? ITabContent.CurrentFilePath => CollectionFilePath;
    string? ITabContent.RequestId => RequestId;
    string ITabContent.DisplayTitle => RequestName ?? "Untitled";
    string? ITabContent.MethodLabel => SelectedMethod.ToString().ToUpperInvariant();
}
