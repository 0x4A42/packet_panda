using PacketPanda.Core.Http;
using ReactiveUI;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Desktop.ViewModels;

public sealed class RunResultViewModel : ReactiveObject
{
    private RunStatus _status = RunStatus.Pending;
    private int? _statusCode;
    private long? _durationMs;
    private int _testsPassed;
    private int _testsTotal;

    public string RequestName { get; init; } = string.Empty;
    public HttpMethod Method { get; init; }

    public RunStatus Status
    {
        get => _status;
        set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(StatusLabel));
            this.RaisePropertyChanged(nameof(TestsLabel));
        }
    }

    public int? StatusCode
    {
        get => _statusCode;
        set => this.RaiseAndSetIfChanged(ref _statusCode, value);
    }

    public long? DurationMs
    {
        get => _durationMs;
        set
        {
            this.RaiseAndSetIfChanged(ref _durationMs, value);
            this.RaisePropertyChanged(nameof(DurationLabel));
        }
    }

    public int TestsPassed
    {
        get => _testsPassed;
        set
        {
            this.RaiseAndSetIfChanged(ref _testsPassed, value);
            this.RaisePropertyChanged(nameof(TestsLabel));
        }
    }

    public int TestsTotal
    {
        get => _testsTotal;
        set
        {
            this.RaiseAndSetIfChanged(ref _testsTotal, value);
            this.RaisePropertyChanged(nameof(TestsLabel));
        }
    }

    public string StatusLabel => Status switch
    {
        RunStatus.Pending => "Pending",
        RunStatus.Running => "Running…",
        RunStatus.Pass    => "Pass",
        RunStatus.Fail    => "Fail",
        RunStatus.Error   => "Error",
        _                 => string.Empty
    };

    public string DurationLabel => DurationMs.HasValue ? $"{DurationMs}ms" : string.Empty;

    public string TestsLabel => TestsTotal == 0
        ? string.Empty
        : $"{TestsPassed}/{TestsTotal}";
}
