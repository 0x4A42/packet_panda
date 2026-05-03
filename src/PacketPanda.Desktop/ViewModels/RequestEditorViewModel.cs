using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly Func<string, string, VariableTarget, Task>? _updateVariable;
    private readonly Action? _openEnvironmentEditor;
    private Environment? _activeEnv;
    private IReadOnlyDictionary<string, string>? _globalsVars;
    private IReadOnlyDictionary<string, string>? _activeEnvVars;
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

    private AuthType _selectedAuthType = AuthType.None;
    private string _apiKeyName = string.Empty;
    private string _apiKeyValue = string.Empty;
    private ApiKeyTarget _apiKeyTarget = ApiKeyTarget.Header;
    private string _bearerToken = string.Empty;
    private string _basicUsername = string.Empty;
    private string _basicPassword = string.Empty;
    private IDisposable? _authDirtySub;
    private IDisposable? _authDirtySubPassword;

    private sealed record RequestSnapshot(
        HttpMethod Method,
        string Url,
        string? Body,
        string HeadersKey,
        string AuthKey);

    public RequestEditorViewModel(
        IHttpRequestSender sender,
        ICollectionFileStore collectionStore,
        IVariableResolver resolver,
        IObservable<EnvironmentContext?> activeEnvironment,
        Func<string, string, VariableTarget, Task>? updateVariable = null,
        Action? openEnvironmentEditor = null)
    {
        _sender = sender;
        _collectionStore = collectionStore;
        _resolver = resolver;
        _updateVariable = updateVariable;
        _openEnvironmentEditor = openEnvironmentEditor;
        _activeEnvSub = activeEnvironment.Subscribe(ctx =>
        {
            _activeEnv = ctx?.Merged;
            _globalsVars = ctx?.GlobalsVars;
            _activeEnvVars = ctx?.ActiveEnvVars;
            this.RaisePropertyChanged(nameof(ActiveEnvironmentVariables));
            this.RaisePropertyChanged(nameof(GlobalsVariables));
            this.RaisePropertyChanged(nameof(ActiveEnvVariables));
        });

        var canSend = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));
        var canSave = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));

        SendCommand = ReactiveCommand.CreateFromTask(SendAsync, canSend);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);

        _dirtySub = this.WhenAnyValue(vm => vm.SelectedMethod, vm => vm.Url, vm => vm.RequestBody)
            .Subscribe(_ => RecomputeDirty());

        AddHeaderCommand = ReactiveCommand.Create(() =>
            Headers.Add(new HeaderRowViewModel(row => { Headers.Remove(row); RecomputeDirty(); })));

        void OnHeaderRowChanged(object? s, PropertyChangedEventArgs e) => RecomputeDirty();
        Headers.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (HeaderRowViewModel row in e.NewItems)
                    row.PropertyChanged += OnHeaderRowChanged;
            if (e.OldItems != null)
                foreach (HeaderRowViewModel row in e.OldItems)
                    row.PropertyChanged -= OnHeaderRowChanged;
            RecomputeDirty();
        };

        _authDirtySub = this.WhenAnyValue(
            vm => vm.SelectedAuthType,
            vm => vm.ApiKeyName, vm => vm.ApiKeyValue, vm => vm.ApiKeyTarget,
            vm => vm.BearerToken,
            vm => vm.BasicUsername)
            .Subscribe(_ => RecomputeDirty());

        _authDirtySubPassword = this.WhenAnyValue(vm => vm.BasicPassword)
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
    public IReadOnlyDictionary<string, string>? GlobalsVariables => _globalsVars;
    public IReadOnlyDictionary<string, string>? ActiveEnvVariables => _activeEnvVars;

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

    public ObservableCollection<HeaderRowViewModel> Headers { get; } = new();
    public ReactiveCommand<Unit, Unit> AddHeaderCommand { get; }

    public AuthType SelectedAuthType
    {
        get => _selectedAuthType;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAuthType, value);
            this.RaisePropertyChanged(nameof(IsApiKeyAuth));
            this.RaisePropertyChanged(nameof(IsBearerAuth));
            this.RaisePropertyChanged(nameof(IsBasicAuth));
        }
    }

    public bool IsApiKeyAuth => SelectedAuthType == AuthType.ApiKey;
    public bool IsBearerAuth => SelectedAuthType == AuthType.Bearer;
    public bool IsBasicAuth => SelectedAuthType == AuthType.Basic;

    public string ApiKeyName
    {
        get => _apiKeyName;
        set => this.RaiseAndSetIfChanged(ref _apiKeyName, value);
    }

    public string ApiKeyValue
    {
        get => _apiKeyValue;
        set => this.RaiseAndSetIfChanged(ref _apiKeyValue, value);
    }

    public ApiKeyTarget ApiKeyTarget
    {
        get => _apiKeyTarget;
        set => this.RaiseAndSetIfChanged(ref _apiKeyTarget, value);
    }

    public string BearerToken
    {
        get => _bearerToken;
        set => this.RaiseAndSetIfChanged(ref _bearerToken, value);
    }

    public string BasicUsername
    {
        get => _basicUsername;
        set => this.RaiseAndSetIfChanged(ref _basicUsername, value);
    }

    public string BasicPassword
    {
        get => _basicPassword;
        set => this.RaiseAndSetIfChanged(ref _basicPassword, value);
    }

    public AuthType[] AvailableAuthTypes { get; } =
        [AuthType.None, AuthType.ApiKey, AuthType.Bearer, AuthType.Basic];

    public ApiKeyTarget[] AvailableApiKeyTargets { get; } =
        [ApiKeyTarget.Header, ApiKeyTarget.QueryParam];

    public HttpMethod[] AvailableMethods { get; } =
    {
        HttpMethod.Get,
        HttpMethod.Post,
        HttpMethod.Put,
        HttpMethod.Delete
    };

    public Task UpdateVariableAsync(string name, string value, VariableTarget target) =>
        _updateVariable?.Invoke(name, value, target) ?? Task.CompletedTask;

    public void OpenEnvironmentEditor() => _openEnvironmentEditor?.Invoke();

    public HttpRequest BuildRequest()
    {
        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        var headers = Headers.Select(h => new HttpHeader(h.Name, h.Value, h.Enabled)).ToList();
        AuthConfig auth = SelectedAuthType switch
        {
            AuthType.Bearer => new BearerAuth(BearerToken),
            AuthType.ApiKey => new ApiKeyAuth(ApiKeyName, ApiKeyValue, ApiKeyTarget),
            AuthType.Basic  => new BasicAuth(BasicUsername, BasicPassword),
            _               => new NoAuth()
        };
        return new HttpRequest(SelectedMethod, Url, body, headers, auth);
    }

    public void LoadHeaders(IReadOnlyList<HttpHeader> headers)
    {
        Headers.Clear();
        foreach (var h in headers)
            Headers.Add(new HeaderRowViewModel(row => { Headers.Remove(row); RecomputeDirty(); })
                { Enabled = h.Enabled, Name = h.Name, Value = h.Value });
    }

    public void LoadAuth(AuthConfig auth)
    {
        switch (auth)
        {
            case BearerAuth bearer:
                SelectedAuthType = AuthType.Bearer;
                BearerToken = bearer.Token;
                break;
            case ApiKeyAuth apiKey:
                SelectedAuthType = AuthType.ApiKey;
                ApiKeyName = apiKey.Name;
                ApiKeyValue = apiKey.Value;
                ApiKeyTarget = apiKey.Target;
                break;
            case BasicAuth basic:
                SelectedAuthType = AuthType.Basic;
                BasicUsername = basic.Username;
                BasicPassword = basic.Password;
                break;
            default:
                SelectedAuthType = AuthType.None;
                break;
        }
    }

    private async Task SendAsync()
    {
        ErrorMessage = null;

        var urlResult = _resolver.Resolve(Url, _activeEnv);
        var bodyResult = _resolver.Resolve(RequestBody ?? string.Empty, _activeEnv);
        var unresolvedNames = new List<string>(urlResult.UnresolvedNames.Concat(bodyResult.UnresolvedNames));

        var resolvedHeaders = new List<HttpHeader>();
        foreach (var row in Headers.Where(h => h.Enabled))
        {
            var r = _resolver.Resolve(row.Value, _activeEnv);
            unresolvedNames.AddRange(r.UnresolvedNames);
            resolvedHeaders.Add(new HttpHeader(row.Name, r.Text));
        }

        AuthConfig resolvedAuth;
        switch (SelectedAuthType)
        {
            case AuthType.Bearer:
            {
                var r = _resolver.Resolve(BearerToken, _activeEnv);
                unresolvedNames.AddRange(r.UnresolvedNames);
                resolvedAuth = new BearerAuth(r.Text);
                break;
            }
            case AuthType.ApiKey:
            {
                var r = _resolver.Resolve(ApiKeyValue, _activeEnv);
                unresolvedNames.AddRange(r.UnresolvedNames);
                resolvedAuth = new ApiKeyAuth(ApiKeyName, r.Text, ApiKeyTarget);
                break;
            }
            case AuthType.Basic:
            {
                var ru = _resolver.Resolve(BasicUsername, _activeEnv);
                var rp = _resolver.Resolve(BasicPassword, _activeEnv);
                unresolvedNames.AddRange(ru.UnresolvedNames);
                unresolvedNames.AddRange(rp.UnresolvedNames);
                resolvedAuth = new BasicAuth(ru.Text, rp.Text);
                break;
            }
            default:
                resolvedAuth = new NoAuth();
                break;
        }

        var unresolved = unresolvedNames.Distinct(StringComparer.Ordinal).ToList();
        if (unresolved.Count > 0)
        {
            ErrorMessage = $"Unresolved variables: {string.Join(", ", unresolved)}";
            StatusCode = null; ResponseBody = null; ElapsedMilliseconds = null;
            return;
        }

        var body = string.IsNullOrEmpty(bodyResult.Text) ? null : bodyResult.Text;
        var result = await _sender.SendAsync(
            new HttpRequest(SelectedMethod, urlResult.Text, body, resolvedHeaders, resolvedAuth));

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
            await _collectionStore.UpdateRequestAsync(CollectionFilePath, RequestId, BuildRequest());
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
        _authDirtySub?.Dispose();
        _authDirtySubPassword?.Dispose();
    }

    private void RecomputeDirty()
    {
        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        IsDirty = _savedSnapshot != new RequestSnapshot(
            SelectedMethod, Url, body, ComputeHeadersKey(), ComputeAuthKey());
    }

    private string ComputeHeadersKey() =>
        string.Join("|", Headers.Select(h => $"{h.Enabled}:{h.Name}:{h.Value}"));

    private string ComputeAuthKey() => SelectedAuthType switch
    {
        AuthType.ApiKey => $"apikey:{ApiKeyName}:{ApiKeyValue}:{ApiKeyTarget}",
        AuthType.Bearer => $"bearer:{BearerToken}",
        AuthType.Basic  => $"basic:{BasicUsername}:{BasicPassword}",
        _               => "none"
    };

    private void CaptureSnapshot()
    {
        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        _savedSnapshot = new RequestSnapshot(SelectedMethod, Url, body, ComputeHeadersKey(), ComputeAuthKey());
        IsDirty = false;
    }

    public void MarkSaved() => CaptureSnapshot();

    // ITabContent
    string? ITabContent.CurrentFilePath => CollectionFilePath;
    string? ITabContent.RequestId => RequestId;
    string ITabContent.DisplayTitle => RequestName ?? "Untitled";
    string? ITabContent.MethodLabel => SelectedMethod.ToString().ToUpperInvariant();
}
