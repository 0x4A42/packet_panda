using System.Collections.ObjectModel;
using System.Reactive;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Core.Scripting;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public sealed class CollectionRunnerViewModel : ReactiveObject, ITabContent, IDisposable
{
    private readonly FolderNode _folder;
    private readonly IHttpRequestSender _sender;
    private readonly IVariableResolver _resolver;
    private readonly Func<string, string, VariableTarget, Task>? _updateVariable;
    private readonly IDisposable _envSub;
    private readonly List<RequestNode> _flatRequests;

    private bool _isRunning;
    private string _summaryText = string.Empty;
    private EnvironmentContext? _currentEnvCtx;
    private RunResultViewModel? _selectedResult;

    public CollectionRunnerViewModel(
        FolderNode folder,
        IHttpRequestSender sender,
        IVariableResolver resolver,
        IObservable<EnvironmentContext?> activeEnvironment,
        Func<string, string, VariableTarget, Task>? updateVariable = null,
        Func<RequestNode, Task>? openRequest = null)
    {
        _folder = folder;
        _sender = sender;
        _resolver = resolver;
        _updateVariable = updateVariable;
        _envSub = activeEnvironment.Subscribe(ctx => _currentEnvCtx = ctx);

        _flatRequests = FlattenFolder(folder).ToList();
        foreach (var r in _flatRequests)
            RunResults.Add(new RunResultViewModel { RequestName = r.Name, Method = r.Method, Node = r });

        RunCommand = ReactiveCommand.CreateFromTask(
            ExecuteRunAsync,
            this.WhenAnyValue(vm => vm.IsRunning, running => !running));

        ReRunCommand = ReactiveCommand.CreateFromTask<RunResultViewModel>(
            ReRunRequestAsync,
            this.WhenAnyValue(vm => vm.IsRunning, running => !running));

        GoToRequestCommand = ReactiveCommand.CreateFromTask<RunResultViewModel>(
            async row =>
            {
                if (row.Node is not null && openRequest is not null)
                    await openRequest(row.Node);
            });
    }

    public ObservableCollection<RunResultViewModel> RunResults { get; } = new();
    public ReactiveCommand<Unit, Unit> RunCommand { get; }
    public ReactiveCommand<RunResultViewModel, Unit> ReRunCommand { get; }
    public ReactiveCommand<RunResultViewModel, Unit> GoToRequestCommand { get; }

    public RunResultViewModel? SelectedResult
    {
        get => _selectedResult;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedResult, value);
            this.RaisePropertyChanged(nameof(HasSelectedResult));
        }
    }

    public bool HasSelectedResult => _selectedResult is not null;

    public bool IsRunning
    {
        get => _isRunning;
        private set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => this.RaiseAndSetIfChanged(ref _summaryText, value);
    }

    // ITabContent
    public string CurrentFilePath => _folder.Id;
    public string? RequestId => null;
    public string DisplayTitle => $"Runner: {_folder.Name}";
    public bool IsDirty => false;
    public string? MethodLabel => null;

    private async Task ExecuteRunAsync()
    {
        IsRunning = true;

        foreach (var row in RunResults)
        {
            row.Status = RunStatus.Pending;
            row.StatusCode = null;
            row.DurationMs = null;
            row.TestsPassed = 0;
            row.TestsTotal = 0;
            row.ErrorMessage = null;
            row.ResponseBody = null;
            row.ResponseHeaders = null;
        }

        var passed = 0;
        var failed = 0;
        var errored = 0;

        for (var i = 0; i < _flatRequests.Count; i++)
        {
            var req = _flatRequests[i];
            var row = RunResults[i];
            await RunSingleRequestAsync(req, row);

            switch (row.Status)
            {
                case RunStatus.Pass: passed++; break;
                case RunStatus.Fail: failed++; break;
                case RunStatus.Error: errored++; break;
            }
        }

        SummaryText = $"{passed} passed · {failed} failed · {errored} errors · {_flatRequests.Count} total";
        IsRunning = false;
    }

    private async Task RunSingleRequestAsync(RequestNode req, RunResultViewModel row)
    {
        row.Status = RunStatus.Running;
        try
        {
            var env = _currentEnvCtx?.Merged;
            var urlResult = _resolver.Resolve(req.Url, env);
            var httpRequest = new HttpRequest(req.Method, urlResult.Text, req.Body);

            using var session = new ScriptSession(env);

            if (!string.IsNullOrWhiteSpace(req.PreRequestScript))
                session.RunPreRequest(req.PreRequestScript);

            var sendResult = await _sender.SendAsync(httpRequest);

            switch (sendResult)
            {
                case SendResult.Ok ok:
                    if (!string.IsNullOrWhiteSpace(req.PostResponseScript))
                        session.RunPostResponse(req.PostResponseScript, ok.Response);

                    if (_updateVariable is not null)
                        foreach (var (key, value) in session.EnvironmentUpdates)
                            await _updateVariable(key, value, VariableTarget.ActiveEnv);

                    var testResults = session.TestResults.ToList();
                    row.StatusCode = ok.Response.StatusCode;
                    row.DurationMs = ok.Response.ElapsedMilliseconds;
                    row.ResponseBody = ResponseFormatter.MaybePrettyPrint(ok.Response.Body, ok.Response.Headers);
                    row.ResponseHeaders = ok.Response.Headers;
                    row.TestsTotal = testResults.Count;
                    row.TestsPassed = testResults.Count(t => t.Passed);

                    row.Status = (testResults.Any(t => !t.Passed) || ok.Response.StatusCode >= 400)
                        ? RunStatus.Fail
                        : RunStatus.Pass;
                    break;

                case SendResult.Error err:
                    row.Status = RunStatus.Error;
                    row.ErrorMessage = err.Message;
                    break;
            }
        }
        catch (Exception ex)
        {
            row.Status = RunStatus.Error;
            row.ErrorMessage = ex.Message;
        }
    }

    private async Task ReRunRequestAsync(RunResultViewModel row)
    {
        var i = RunResults.IndexOf(row);
        if (i < 0 || i >= _flatRequests.Count) return;

        row.Status = RunStatus.Pending;
        row.StatusCode = null;
        row.DurationMs = null;
        row.TestsPassed = 0;
        row.TestsTotal = 0;
        row.ErrorMessage = null;
        row.ResponseBody = null;
        row.ResponseHeaders = null;

        IsRunning = true;
        await RunSingleRequestAsync(_flatRequests[i], row);
        IsRunning = false;
    }

    public void Dispose() => _envSub.Dispose();

    private static IEnumerable<RequestNode> FlattenFolder(FolderNode folder)
    {
        foreach (var child in folder.Children)
        {
            if (child is RequestNode req)
                yield return req;
            else if (child is FolderNode sub)
                foreach (var r in FlattenFolder(sub))
                    yield return r;
        }
    }
}
