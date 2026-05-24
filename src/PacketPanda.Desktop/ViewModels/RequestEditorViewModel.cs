using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive;
using PacketPanda.Core.Environments;
using PacketPanda.Core.History;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Core.Scripting;
using PacketPanda.Desktop.Services;
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
    private BodyMode _selectedBodyMode = BodyMode.None;
    private string _rawBodyText = string.Empty;
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
    private readonly System.Reactive.Subjects.Subject<string> _savedSuccessfully = new();
    private readonly IHistoryStore? _historyStore;
    private readonly System.Reactive.Subjects.Subject<HistoryEntry> _historyEntryRecorded = new();
    private readonly IClipboardService? _clipboard;

    private bool _syncingParams;
    private IDisposable? _urlParamsSyncSub;

    private string _preRequestScript = string.Empty;
    private string _postResponseScript = string.Empty;
    private ScriptSection _selectedScriptSection = ScriptSection.PreRequest;

    private sealed record RequestSnapshot(
        HttpMethod Method,
        string Url,
        BodyMode BodyMode,
        string RawBodyText,
        string FormKey,
        string HeadersKey,
        string AuthKey,
        string ParamsKey,
        string PreRequestScript,
        string PostResponseScript);

    public RequestEditorViewModel(
        IHttpRequestSender sender,
        ICollectionFileStore collectionStore,
        IVariableResolver resolver,
        IObservable<EnvironmentContext?> activeEnvironment,
        Func<string, string, VariableTarget, Task>? updateVariable = null,
        Action? openEnvironmentEditor = null,
        IHistoryStore? historyStore = null,
        IClipboardService? clipboardService = null)
    {
        _sender = sender;
        _collectionStore = collectionStore;
        _resolver = resolver;
        _updateVariable = updateVariable;
        _openEnvironmentEditor = openEnvironmentEditor;
        _historyStore = historyStore;
        _clipboard = clipboardService;
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
        SelectPreRequestCommand = ReactiveCommand.Create(() => { SelectedScriptSection = ScriptSection.PreRequest; });
        SelectPostResponseCommand = ReactiveCommand.Create(() => { SelectedScriptSection = ScriptSection.PostResponse; });
        CopyCurlCommand = ReactiveCommand.CreateFromTask(CopyCurlAsync);

        _dirtySub = this.WhenAnyValue(vm => vm.SelectedMethod, vm => vm.Url, vm => vm.RawBodyText, vm => vm.SelectedBodyMode)
            .Subscribe(_ => RecomputeDirty());

        AddHeaderCommand = ReactiveCommand.Create(() => Headers.Add(CreateHeaderRow()));

        void OnHeaderRowChanged(object? s, PropertyChangedEventArgs e)
        {
            EnsureTrailingEmptyRow();
            RecomputeDirty();
        }
        Headers.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (HeaderRowViewModel row in e.NewItems)
                    row.PropertyChanged += OnHeaderRowChanged;
            if (e.OldItems is not null)
                foreach (HeaderRowViewModel row in e.OldItems)
                    row.PropertyChanged -= OnHeaderRowChanged;
            RecomputeDirty();
        };
        EnsureTrailingEmptyRow();

        _authDirtySub = this.WhenAnyValue(
            vm => vm.SelectedAuthType,
            vm => vm.ApiKeyName, vm => vm.ApiKeyValue, vm => vm.ApiKeyTarget,
            vm => vm.BearerToken,
            vm => vm.BasicUsername)
            .Subscribe(_ => RecomputeDirty());

        _authDirtySubPassword = this.WhenAnyValue(vm => vm.BasicPassword)
            .Subscribe(_ => RecomputeDirty());

        AddQueryParamCommand = ReactiveCommand.Create(() => QueryParams.Add(CreateParamRow()));

        void OnParamRowChanged(object? s, PropertyChangedEventArgs e)
        {
            EnsureTrailingEmptyParamRow();
            if (!_syncingParams) SyncUrlFromParams();
            RecomputeDirty();
        }
        QueryParams.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (QueryParamRowViewModel row in e.NewItems)
                    row.PropertyChanged += OnParamRowChanged;
            if (e.OldItems is not null)
                foreach (QueryParamRowViewModel row in e.OldItems)
                    row.PropertyChanged -= OnParamRowChanged;
            RecomputeDirty();
        };
        EnsureTrailingEmptyParamRow();

        _urlParamsSyncSub = this.WhenAnyValue(vm => vm.Url)
            .Subscribe(_ => SyncParamsFromUrl());

        FormRows = new ObservableCollection<FormRowViewModel>();
        void OnFormRowChanged(object? s, PropertyChangedEventArgs e)
        {
            EnsureTrailingEmptyFormRow();
            RecomputeDirty();
        }
        FormRows.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (FormRowViewModel row in e.NewItems)
                    row.PropertyChanged += OnFormRowChanged;
            if (e.OldItems is not null)
                foreach (FormRowViewModel row in e.OldItems)
                    row.PropertyChanged -= OnFormRowChanged;
            RecomputeDirty();
        };
        EnsureTrailingEmptyFormRow();

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
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMethod, value);
            this.RaisePropertyChanged(nameof(IsBodyEnabled));
        }
    }

    public string Url
    {
        get => _url;
        set => this.RaiseAndSetIfChanged(ref _url, value);
    }

    public BodyMode SelectedBodyMode
    {
        get => _selectedBodyMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedBodyMode, value);
            this.RaisePropertyChanged(nameof(IsRawBodyMode));
            this.RaisePropertyChanged(nameof(IsFormBodyMode));
            this.RaisePropertyChanged(nameof(IsNoneBodyMode));
            RecomputeDirty();
        }
    }

    public string RawBodyText
    {
        get => _rawBodyText;
        set => this.RaiseAndSetIfChanged(ref _rawBodyText, value);
    }

    public ObservableCollection<FormRowViewModel> FormRows { get; } = new();

    public bool IsRawBodyMode  => SelectedBodyMode is BodyMode.Json or BodyMode.Text or BodyMode.Xml;
    public bool IsFormBodyMode => SelectedBodyMode == BodyMode.Form;
    public bool IsNoneBodyMode => SelectedBodyMode == BodyMode.None;
    public bool IsBodyEnabled  => SelectedMethod != HttpMethod.Head;

    public BodyMode[] AvailableBodyModes { get; } =
        [BodyMode.None, BodyMode.Json, BodyMode.Text, BodyMode.Xml, BodyMode.Form];

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
        set
        {
            this.RaiseAndSetIfChanged(ref _responseBody, value);
            this.RaisePropertyChanged(nameof(ResponseSizeText));
        }
    }

    public string? ResponseSizeText =>
        _responseBody is null
            ? null
            : FormatBytes(System.Text.Encoding.UTF8.GetByteCount(_responseBody));

    private static string FormatBytes(long bytes) =>
        bytes < 1024
            ? $"{bytes} B"
            : $"{bytes / 1024.0:0.#} KB";

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
    public ReactiveCommand<Unit, Unit> SelectPreRequestCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectPostResponseCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyCurlCommand { get; }

    public ObservableCollection<HeaderRowViewModel> Headers { get; } = new();
    public ReactiveCommand<Unit, Unit> AddHeaderCommand { get; }
    public IObservable<string> SavedSuccessfully => _savedSuccessfully;
    public IObservable<HistoryEntry> HistoryEntryRecorded => _historyEntryRecorded;

    public ObservableCollection<QueryParamRowViewModel> QueryParams { get; } = new();
    public ReactiveCommand<Unit, Unit> AddQueryParamCommand { get; }

    public ObservableCollection<HttpHeader> ResponseHeaders { get; } = new();
    public ObservableCollection<CookieViewModel> ResponseCookies { get; } = new();

    private string _responseHeadersTabLabel = "Headers";
    public string ResponseHeadersTabLabel
    {
        get => _responseHeadersTabLabel;
        private set => this.RaiseAndSetIfChanged(ref _responseHeadersTabLabel, value);
    }

    public string PreRequestScript
    {
        get => _preRequestScript;
        set
        {
            this.RaiseAndSetIfChanged(ref _preRequestScript, value);
            RecomputeDirty();
        }
    }

    public string PostResponseScript
    {
        get => _postResponseScript;
        set
        {
            this.RaiseAndSetIfChanged(ref _postResponseScript, value);
            RecomputeDirty();
        }
    }

    public ScriptSection SelectedScriptSection
    {
        get => _selectedScriptSection;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedScriptSection, value);
            this.RaisePropertyChanged(nameof(IsPreRequestScript));
            this.RaisePropertyChanged(nameof(IsPostResponseScript));
        }
    }

    public bool IsPreRequestScript => SelectedScriptSection == ScriptSection.PreRequest;
    public bool IsPostResponseScript => SelectedScriptSection == ScriptSection.PostResponse;

    public ObservableCollection<TestResultViewModel> TestResults { get; } = new();

    private string _testsTabLabel = "Tests";
    public string TestsTabLabel
    {
        get => _testsTabLabel;
        private set => this.RaiseAndSetIfChanged(ref _testsTabLabel, value);
    }

    private void RefreshResponseTabLabels()
    {
        TestsTabLabel = TestResults.Count == 0
            ? "Tests"
            : $"Tests ({TestResults.Count(r => r.Passed)}/{TestResults.Count})";
        ResponseHeadersTabLabel = ResponseHeaders.Count == 0
            ? "Headers"
            : $"Headers ({ResponseHeaders.Count})";
    }

    public AuthType SelectedAuthType
    {
        get => _selectedAuthType;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAuthType, value);
            this.RaisePropertyChanged(nameof(IsApiKeyAuth));
            this.RaisePropertyChanged(nameof(IsBearerAuth));
            this.RaisePropertyChanged(nameof(IsBasicAuth));
            this.RaisePropertyChanged(nameof(IsNoAuth));
        }
    }

    public bool IsApiKeyAuth => SelectedAuthType == AuthType.ApiKey;
    public bool IsBearerAuth => SelectedAuthType == AuthType.Bearer;
    public bool IsBasicAuth => SelectedAuthType == AuthType.Basic;
    public bool IsNoAuth => SelectedAuthType == AuthType.None;

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
        HttpMethod.Delete,
        HttpMethod.Patch,
        HttpMethod.Head,
        HttpMethod.Options
    };

    public Task UpdateVariableAsync(string name, string value, VariableTarget target) =>
        _updateVariable?.Invoke(name, value, target) ?? Task.CompletedTask;

    public void OpenEnvironmentEditor() => _openEnvironmentEditor?.Invoke();

    public HttpRequest BuildRequest()
    {
        var headers = Headers
            .Where(h => !string.IsNullOrEmpty(h.Name))
            .Select(h => new HttpHeader(h.Name, h.Value, h.Enabled))
            .ToList();
        AuthConfig auth = SelectedAuthType switch
        {
            AuthType.Bearer => new BearerAuth(BearerToken),
            AuthType.ApiKey => new ApiKeyAuth(ApiKeyName, ApiKeyValue, ApiKeyTarget),
            AuthType.Basic  => new BasicAuth(BasicUsername, BasicPassword),
            _               => new NoAuth()
        };
        var queryParams = QueryParams
            .Where(p => p.Enabled && !string.IsNullOrEmpty(p.Key))
            .Select(p => new QueryParam(p.Key, p.Value, p.Enabled))
            .ToList();
        return new HttpRequest(SelectedMethod, Url, BuildBodyConfig(), headers, auth, queryParams);
    }

    private BodyConfig BuildBodyConfig() => SelectedBodyMode switch
    {
        BodyMode.Json => new JsonBody(RawBodyText),
        BodyMode.Text => new PlainTextBody(RawBodyText),
        BodyMode.Xml  => new XmlBody(RawBodyText),
        BodyMode.Form => new FormBody(
            FormRows
                .Where(r => !string.IsNullOrEmpty(r.Name))
                .Select(r => new FormField(r.Name, r.Value, r.Enabled))
                .ToList()),
        _ => new NoBody()
    };

    public void LoadHeaders(IReadOnlyList<HttpHeader> headers)
    {
        Headers.Clear();
        foreach (var h in headers)
        {
            var row = CreateHeaderRow();
            row.Enabled = h.Enabled;
            row.Name = h.Name;
            row.Value = h.Value;
            Headers.Add(row);
        }
        EnsureTrailingEmptyRow();
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

    public void LoadQueryParams(IReadOnlyList<QueryParam> queryParams)
    {
        QueryParams.Clear();
        foreach (var qp in queryParams)
        {
            var row = CreateParamRow();
            row.Enabled = qp.Enabled;
            row.Key = qp.Key;
            row.Value = qp.Value;
            QueryParams.Add(row);
        }
        EnsureTrailingEmptyParamRow();
    }

    public void LoadBody(BodyConfig body)
    {
        switch (body)
        {
            case JsonBody json:
                SelectedBodyMode = BodyMode.Json;
                RawBodyText = json.Text;
                break;
            case PlainTextBody text:
                SelectedBodyMode = BodyMode.Text;
                RawBodyText = text.Text;
                break;
            case XmlBody xml:
                SelectedBodyMode = BodyMode.Xml;
                RawBodyText = xml.Text;
                break;
            case FormBody form:
                SelectedBodyMode = BodyMode.Form;
                FormRows.Clear();
                foreach (var f in form.Fields)
                {
                    var row = CreateFormRow();
                    row.Enabled = f.Enabled;
                    row.Name = f.Name;
                    row.Value = f.Value;
                    FormRows.Add(row);
                }
                EnsureTrailingEmptyFormRow();
                break;
            default:
                SelectedBodyMode = BodyMode.None;
                RawBodyText = string.Empty;
                break;
        }
    }

    public void LoadScripts(string? preRequest, string? postResponse)
    {
        PreRequestScript = preRequest ?? string.Empty;
        PostResponseScript = postResponse ?? string.Empty;
        SelectedScriptSection = ScriptSection.PreRequest;
    }

    public void LoadHistoryResponse(HistoryEntry entry)
    {
        StatusCode = entry.StatusCode;
        ResponseBody = ResponseFormatter.MaybePrettyPrint(entry.ResponseBody, entry.ResponseHeaders);
        ElapsedMilliseconds = entry.ElapsedMilliseconds;
        ErrorMessage = entry.ErrorMessage;

        ResponseHeaders.Clear();
        ResponseCookies.Clear();
        if (entry.ResponseHeaders is not null)
        {
            foreach (var h in entry.ResponseHeaders)
            {
                ResponseHeaders.Add(h);
                if (string.Equals(h.Name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    ResponseCookies.Add(CookieViewModel.FromHeader(h.Value));
            }
        }

        TestResults.Clear();
        if (entry.TestResults is not null)
            foreach (var r in entry.TestResults)
                TestResults.Add(new TestResultViewModel(r.Name, r.Passed, r.ErrorMessage));

        RefreshResponseTabLabels();
    }

    private async Task SendAsync()
    {
        ErrorMessage = null;

        var urlResult = _resolver.Resolve(Url, _activeEnv);
        var unresolvedNames = new List<string>(urlResult.UnresolvedNames);

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

        BodyConfig resolvedBody;
        switch (SelectedBodyMode)
        {
            case BodyMode.Json:
            {
                var r = _resolver.Resolve(RawBodyText, _activeEnv);
                unresolvedNames.AddRange(r.UnresolvedNames);
                resolvedBody = new JsonBody(r.Text);
                break;
            }
            case BodyMode.Text:
            {
                var r = _resolver.Resolve(RawBodyText, _activeEnv);
                unresolvedNames.AddRange(r.UnresolvedNames);
                resolvedBody = new PlainTextBody(r.Text);
                break;
            }
            case BodyMode.Xml:
            {
                var r = _resolver.Resolve(RawBodyText, _activeEnv);
                unresolvedNames.AddRange(r.UnresolvedNames);
                resolvedBody = new XmlBody(r.Text);
                break;
            }
            case BodyMode.Form:
            {
                var fields = new List<FormField>();
                foreach (var row in FormRows.Where(r => r.Enabled && !string.IsNullOrEmpty(r.Name)))
                {
                    var r = _resolver.Resolve(row.Value, _activeEnv);
                    unresolvedNames.AddRange(r.UnresolvedNames);
                    fields.Add(new FormField(row.Name, r.Text, true));
                }
                resolvedBody = new FormBody(fields);
                break;
            }
            default:
                resolvedBody = new NoBody();
                break;
        }

        var unresolved = unresolvedNames.Distinct(StringComparer.Ordinal).ToList();
        if (unresolved.Count > 0)
        {
            ErrorMessage = $"Unresolved variables: {string.Join(", ", unresolved)}";
            StatusCode = null; ResponseBody = null; ElapsedMilliseconds = null;
            return;
        }

        // Pre-request script
        using var session = new ScriptSession(_activeEnv);
        if (!string.IsNullOrWhiteSpace(PreRequestScript))
        {
            try { session.RunPreRequest(PreRequestScript); }
            catch (ScriptException ex)
            {
                ErrorMessage = ex.Message;
                StatusCode = null; ResponseBody = null; ElapsedMilliseconds = null;
                return;
            }
        }

        var result = await _sender.SendAsync(
            new HttpRequest(SelectedMethod, urlResult.Text, resolvedBody, resolvedHeaders, resolvedAuth));

        switch (result)
        {
            case SendResult.Ok ok:
                // Post-response script
                if (!string.IsNullOrWhiteSpace(PostResponseScript))
                    session.RunPostResponse(PostResponseScript, ok.Response);

                // Apply any environment writes from scripts
                foreach (var (key, value) in session.EnvironmentUpdates)
                    await (_updateVariable?.Invoke(key, value, VariableTarget.ActiveEnv) ?? Task.CompletedTask);

                TestResults.Clear();
                foreach (var r in session.TestResults)
                    TestResults.Add(new TestResultViewModel(r.Name, r.Passed, r.ErrorMessage));

                StatusCode = ok.Response.StatusCode;
                ResponseBody = ResponseFormatter.MaybePrettyPrint(ok.Response.Body, ok.Response.Headers);
                ElapsedMilliseconds = ok.Response.ElapsedMilliseconds;
                ErrorMessage = null;

                ResponseHeaders.Clear();
                ResponseCookies.Clear();
                if (ok.Response.Headers is not null)
                {
                    foreach (var h in ok.Response.Headers)
                        ResponseHeaders.Add(h);
                    foreach (var h in ok.Response.Headers.Where(h =>
                        string.Equals(h.Name, "Set-Cookie", StringComparison.OrdinalIgnoreCase)))
                        ResponseCookies.Add(CookieViewModel.FromHeader(h.Value));
                }
                RefreshResponseTabLabels();
                break;

            case SendResult.Error err:
                ErrorMessage = err.Message;
                StatusCode = null; ResponseBody = null; ElapsedMilliseconds = null;
                TestResults.Clear();
                ResponseHeaders.Clear();
                ResponseCookies.Clear();
                RefreshResponseTabLabels();
                break;
        }

        RecordHistory(SelectedMethod, urlResult.Text, resolvedHeaders, resolvedBody, resolvedAuth, result);
    }

    private void RecordHistory(
        HttpMethod method,
        string url,
        IReadOnlyList<HttpHeader> headers,
        BodyConfig body,
        AuthConfig auth,
        SendResult result)
    {
        if (_historyStore is null) return;

        var capturedTests = TestResults
            .Select(r => new TestResult(r.Name, r.Passed, r.ErrorMessage))
            .ToList();

        var entry = result switch
        {
            SendResult.Ok ok => new HistoryEntry(
                DateTimeOffset.Now, method, url, headers, body, auth,
                ok.Response.StatusCode, ok.Response.Body, ok.Response.Headers,
                ok.Response.ElapsedMilliseconds, null, capturedTests),
            SendResult.Error err => new HistoryEntry(
                DateTimeOffset.Now, method, url, headers, body, auth,
                null, null, null, null, err.Message),
            _ => null
        };
        if (entry is null) return;

        _historyEntryRecorded.OnNext(entry);
        _ = _historyStore.AppendAsync(entry);   // fire-and-forget, best-effort
    }

    private async Task SaveAsync()
    {
        if (RequestId is null || CollectionFilePath is null) return;
        try
        {
            await _collectionStore.UpdateRequestAsync(
                CollectionFilePath, RequestId, BuildRequest(),
                preRequestScript: PreRequestScript,
                postResponseScript: PostResponseScript);
            CaptureSnapshot();
            ErrorMessage = null;
            _savedSuccessfully.OnNext(CollectionFilePath);
        }
        catch (RequestFileException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task CopyCurlAsync()
    {
        if (_clipboard is null) return;
        var curl = CurlBuilder.Build(BuildResolvedRequest());
        await _clipboard.SetTextAsync(curl);
    }

    private HttpRequest BuildResolvedRequest()
    {
        var url = _resolver.Resolve(Url, _activeEnv).Text;

        var headers = Headers
            .Where(h => h.Enabled && !string.IsNullOrEmpty(h.Name))
            .Select(h => new HttpHeader(h.Name, _resolver.Resolve(h.Value, _activeEnv).Text))
            .ToList();

        AuthConfig auth = SelectedAuthType switch
        {
            AuthType.Bearer => new BearerAuth(_resolver.Resolve(BearerToken, _activeEnv).Text),
            AuthType.ApiKey => new ApiKeyAuth(ApiKeyName, _resolver.Resolve(ApiKeyValue, _activeEnv).Text, ApiKeyTarget),
            AuthType.Basic  => new BasicAuth(
                _resolver.Resolve(BasicUsername, _activeEnv).Text,
                _resolver.Resolve(BasicPassword, _activeEnv).Text),
            _ => new NoAuth()
        };

        BodyConfig body = SelectedBodyMode switch
        {
            BodyMode.Json => new JsonBody(_resolver.Resolve(RawBodyText, _activeEnv).Text),
            BodyMode.Text => new PlainTextBody(_resolver.Resolve(RawBodyText, _activeEnv).Text),
            BodyMode.Xml  => new XmlBody(_resolver.Resolve(RawBodyText, _activeEnv).Text),
            BodyMode.Form => new FormBody(FormRows
                .Where(r => r.Enabled && !string.IsNullOrEmpty(r.Name))
                .Select(r => new FormField(r.Name, _resolver.Resolve(r.Value, _activeEnv).Text, r.Enabled))
                .ToList()),
            _ => new NoBody()
        };

        return new HttpRequest(SelectedMethod, url, body, headers, auth);
    }

    public void Dispose()
    {
        _activeEnvSub?.Dispose();
        _dirtySub?.Dispose();
        _authDirtySub?.Dispose();
        _authDirtySubPassword?.Dispose();
        _urlParamsSyncSub?.Dispose();
        _savedSuccessfully.Dispose();
        _historyEntryRecorded.Dispose();
    }

    private void RecomputeDirty()
    {
        IsDirty = _savedSnapshot != new RequestSnapshot(
            SelectedMethod, Url, SelectedBodyMode, RawBodyText, ComputeFormKey(),
            ComputeHeadersKey(), ComputeAuthKey(), ComputeParamsKey(),
            PreRequestScript, PostResponseScript);
    }

    private HeaderRowViewModel CreateHeaderRow() =>
        new HeaderRowViewModel(row => { Headers.Remove(row); EnsureTrailingEmptyRow(); });

    private void EnsureTrailingEmptyRow()
    {
        var last = Headers.LastOrDefault();
        if (last is null || !string.IsNullOrEmpty(last.Name) || !string.IsNullOrEmpty(last.Value))
            Headers.Add(CreateHeaderRow());
    }

    private string ComputeHeadersKey() =>
        string.Join("|", Headers
            .Where(h => !string.IsNullOrEmpty(h.Name))
            .Select(h => $"{h.Enabled}:{h.Name}:{h.Value}"));

    private string ComputeAuthKey() => SelectedAuthType switch
    {
        AuthType.ApiKey => $"apikey:{ApiKeyName}:{ApiKeyValue}:{ApiKeyTarget}",
        AuthType.Bearer => $"bearer:{BearerToken}",
        AuthType.Basic  => $"basic:{BasicUsername}:{BasicPassword}",
        _               => "none"
    };

    private QueryParamRowViewModel CreateParamRow() =>
        new QueryParamRowViewModel(row => { QueryParams.Remove(row); EnsureTrailingEmptyParamRow(); });

    private void EnsureTrailingEmptyParamRow()
    {
        var last = QueryParams.LastOrDefault();
        if (last is null || !string.IsNullOrEmpty(last.Key) || !string.IsNullOrEmpty(last.Value))
            QueryParams.Add(CreateParamRow());
    }

    private string ComputeParamsKey() =>
        string.Join("|", QueryParams
            .Where(p => !string.IsNullOrEmpty(p.Key))
            .Select(p => $"{p.Enabled}:{p.Key}:{p.Value}"));

    private void SyncUrlFromParams()
    {
        if (_syncingParams) return;
        _syncingParams = true;
        try
        {
            var baseUrl = Url.Contains('?') ? Url[..Url.IndexOf('?')] : Url;
            var enabledParams = QueryParams
                .Where(p => p.Enabled && !string.IsNullOrEmpty(p.Key))
                .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")
                .ToList();
            Url = enabledParams.Count > 0
                ? $"{baseUrl}?{string.Join("&", enabledParams)}"
                : baseUrl;
        }
        finally { _syncingParams = false; }
    }

    private void SyncParamsFromUrl()
    {
        if (_syncingParams) return;
        _syncingParams = true;
        try
        {
            var queryStart = Url.IndexOf('?');
            var pairs = queryStart >= 0
                ? Url[(queryStart + 1)..]
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2 && !string.IsNullOrEmpty(p[0]))
                    .Select(p => (Key: Uri.UnescapeDataString(p[0]), Value: Uri.UnescapeDataString(p[1])))
                    .ToList()
                : new List<(string Key, string Value)>();

            foreach (var row in QueryParams.Where(r => r.Enabled && !string.IsNullOrEmpty(r.Key)).ToList())
            {
                if (!pairs.Any(p => p.Key == row.Key))
                    QueryParams.Remove(row);
            }

            foreach (var (key, value) in pairs)
            {
                var existing = QueryParams.FirstOrDefault(r => r.Enabled && r.Key == key);
                if (existing is not null)
                    existing.Value = value;
                else
                {
                    var row = CreateParamRow();
                    row.Key = key;
                    row.Value = value;
                    var trailingIdx = QueryParams.Count - 1;
                    if (trailingIdx >= 0 && string.IsNullOrEmpty(QueryParams[trailingIdx].Key))
                        QueryParams.Insert(trailingIdx, row);
                    else
                        QueryParams.Add(row);
                }
            }

            EnsureTrailingEmptyParamRow();
        }
        finally { _syncingParams = false; }
    }

    private void CaptureSnapshot()
    {
        _savedSnapshot = new RequestSnapshot(
            SelectedMethod, Url, SelectedBodyMode, RawBodyText, ComputeFormKey(),
            ComputeHeadersKey(), ComputeAuthKey(), ComputeParamsKey(),
            PreRequestScript, PostResponseScript);
        IsDirty = false;
    }

    private string ComputeFormKey() =>
        string.Join("|", FormRows
            .Where(r => !string.IsNullOrEmpty(r.Name))
            .Select(r => $"{r.Enabled}:{r.Name}:{r.Value}"));

    private FormRowViewModel CreateFormRow() =>
        new FormRowViewModel(row => { FormRows.Remove(row); EnsureTrailingEmptyFormRow(); });

    private void EnsureTrailingEmptyFormRow()
    {
        var last = FormRows.LastOrDefault();
        if (last is null || !string.IsNullOrEmpty(last.Name) || !string.IsNullOrEmpty(last.Value))
            FormRows.Add(CreateFormRow());
    }

    public void MarkSaved() => CaptureSnapshot();

    // ITabContent
    string? ITabContent.CurrentFilePath => CollectionFilePath;
    string? ITabContent.RequestId => RequestId;
    string ITabContent.DisplayTitle => RequestName ?? "Untitled";
    string ITabContent.MethodLabel => SelectedMethod.ToString().ToUpperInvariant();
}
