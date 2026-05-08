using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Core.Scripting;
using ReactiveUI;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Desktop.ViewModels;

public sealed class CollectionRunnerViewModel : ReactiveObject, ITabContent, IDisposable
{
    private readonly FolderNode _folder;
    private readonly IHttpRequestSender _sender;
    private readonly IVariableResolver _resolver;
    private readonly Func<string, string, VariableTarget, Task>? _updateVariable;
    private readonly IDisposable _envSub;

    private bool _isRunning;
    private string _summaryText = string.Empty;
    private EnvironmentContext? _currentEnvCtx;
    private RunResultViewModel? _selectedResult;

    public CollectionRunnerViewModel(
        FolderNode folder,
        IHttpRequestSender sender,
        IVariableResolver resolver,
        IObservable<EnvironmentContext?> activeEnvironment,
        Func<string, string, VariableTarget, Task>? updateVariable = null)
    {
        _folder = folder;
        _sender = sender;
        _resolver = resolver;
        _updateVariable = updateVariable;
        _envSub = activeEnvironment.Subscribe(ctx => _currentEnvCtx = ctx);

        var results = FlattenFolder(folder).Select(r => new RunResultViewModel
        {
            RequestName = r.Name,
            Method = r.Method
        });
        foreach (var r in results)
            RunResults.Add(r);

        RunCommand = ReactiveCommand.CreateFromTask(
            ExecuteRunAsync,
            this.WhenAnyValue(vm => vm.IsRunning, running => !running));
    }

    public ObservableCollection<RunResultViewModel> RunResults { get; } = new();
    public ReactiveCommand<Unit, Unit> RunCommand { get; }

    public RunResultViewModel? SelectedResult
    {
        get => _selectedResult;
        set => this.RaiseAndSetIfChanged(ref _selectedResult, value);
    }

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
    public string? CurrentFilePath => _folder.Id;
    public string? RequestId => null;
    public string DisplayTitle => $"Runner: {_folder.Name}";
    public bool IsDirty => false;
    public string? MethodLabel => null;

    private async Task ExecuteRunAsync()
    {
        IsRunning = true;

        var flatRequests = FlattenFolder(_folder).ToList();

        foreach (var row in RunResults)
        {
            row.Status = RunStatus.Pending;
            row.StatusCode = null;
            row.DurationMs = null;
            row.TestsPassed = 0;
            row.TestsTotal = 0;
            row.ErrorMessage = null;
        }

        var passed = 0;
        var failed = 0;
        var errored = 0;

        for (var i = 0; i < flatRequests.Count; i++)
        {
            var req = flatRequests[i];
            var row = RunResults[i];
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
                        row.ResponseBody = ok.Response.Body;
                        row.ResponseHeaders = ok.Response.Headers;
                        row.TestsTotal = testResults.Count;
                        row.TestsPassed = testResults.Count(t => t.Passed);

                        if (testResults.Any(t => !t.Passed))
                        {
                            row.Status = RunStatus.Fail;
                            failed++;
                        }
                        else
                        {
                            row.Status = RunStatus.Pass;
                            passed++;
                        }
                        break;

                    case SendResult.Error err:
                        row.Status = RunStatus.Error;
                        row.ErrorMessage = err.Message;
                        errored++;
                        break;
                }
            }
            catch (Exception ex)
            {
                row.Status = RunStatus.Error;
                row.ErrorMessage = ex.Message;
                errored++;
            }
        }

        SummaryText = $"{passed} passed · {failed} failed · {errored} errors · {flatRequests.Count} total";
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
