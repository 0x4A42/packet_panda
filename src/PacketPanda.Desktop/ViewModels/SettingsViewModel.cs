using System.Reactive;
using PacketPanda.Core.Persistence;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _store;
    private readonly AppSettings _original;

    private bool _isDarkTheme;
    private bool _sslVerification;
    private bool _followRedirects;
    private int _timeoutSeconds;
    private int _historyLimit;

    public SettingsViewModel(ISettingsStore store, AppSettings current)
    {
        _store = store;
        _original = current;

        _isDarkTheme = current.IsDarkTheme;
        _sslVerification = current.SslVerification;
        _followRedirects = current.FollowRedirects;
        _timeoutSeconds = current.TimeoutSeconds;
        _historyLimit = current.HistoryLimit;

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public bool SslVerification
    {
        get => _sslVerification;
        set => this.RaiseAndSetIfChanged(ref _sslVerification, value);
    }

    public bool FollowRedirects
    {
        get => _followRedirects;
        set => this.RaiseAndSetIfChanged(ref _followRedirects, value);
    }

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref _timeoutSeconds, value);
    }

    public int HistoryLimit
    {
        get => _historyLimit;
        set => this.RaiseAndSetIfChanged(ref _historyLimit, value);
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    private async Task SaveAsync()
    {
        var updated = _original with
        {
            IsDarkTheme = IsDarkTheme,
            SslVerification = SslVerification,
            FollowRedirects = FollowRedirects,
            TimeoutSeconds = Math.Clamp(TimeoutSeconds, 1, 300),
            HistoryLimit = Math.Clamp(HistoryLimit, 50, 1000),
        };
        await _store.SaveAsync(updated);

        if (Avalonia.Application.Current is App app)
            app.ApplyTheme(IsDarkTheme);
    }
}
