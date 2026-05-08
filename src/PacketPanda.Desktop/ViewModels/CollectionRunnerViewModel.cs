using System.Collections.ObjectModel;
using System.Reactive;
using PacketPanda.Core.Environments;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Core.Scripting;
using ReactiveUI;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Desktop.ViewModels;

public sealed class CollectionRunnerViewModel : ReactiveObject, ITabContent
{
    private readonly FolderNode _folder;
    private readonly IHttpRequestSender _sender;
    private readonly IVariableResolver _resolver;
    private readonly IObservable<EnvironmentContext?> _activeEnvironment;

    private bool _isRunning;
    private string _summaryText = string.Empty;
    private EnvironmentContext? _currentEnvCtx;

    public CollectionRunnerViewModel(
        FolderNode folder,
        IHttpRequestSender sender,
        IVariableResolver resolver,
        IObservable<EnvironmentContext?> activeEnvironment)
    {
        _folder = folder;
        _sender = sender;
        _resolver = resolver;
        _activeEnvironment = activeEnvironment;
        _activeEnvironment.Subscribe(ctx => _currentEnvCtx = ctx);

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

        RunResults.Clear();
        foreach (var req in flatRequests)
            RunResults.Add(new RunResultViewModel { RequestName = req.Name, Method = req.Method });

        var passed = 0;
        var failed = 0;

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

                        var testResults = session.TestResults.ToList();
                        row.StatusCode = ok.Response.StatusCode;
                        row.DurationMs = ok.Response.ElapsedMilliseconds;
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

                    case SendResult.Error:
                        row.Status = RunStatus.Error;
                        failed++;
                        break;
                }
            }
            catch
            {
                row.Status = RunStatus.Error;
                failed++;
            }
        }

        SummaryText = $"{passed} passed · {failed} failed · {flatRequests.Count} total";
        IsRunning = false;
    }

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
