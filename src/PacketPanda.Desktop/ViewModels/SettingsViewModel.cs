using System.Reactive;
using PacketPanda.Core.Persistence;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _store;

    private bool _isDarkTheme;
    private bool _sslVerification;
    private bool _followRedirects;
    private decimal? _timeoutSeconds;
    private decimal? _historyLimit;

    public SettingsViewModel(ISettingsStore store, AppSettings current)
    {
        _store = store;

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

    public decimal? TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref _timeoutSeconds, value);
    }

    public decimal? HistoryLimit
    {
        get => _historyLimit;
        set => this.RaiseAndSetIfChanged(ref _historyLimit, value);
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    private async Task SaveAsync()
    {
        var current = await _store.LoadAsync();
        var updated = current with
        {
            IsDarkTheme = IsDarkTheme,
            SslVerification = SslVerification,
            FollowRedirects = FollowRedirects,
            TimeoutSeconds = Math.Clamp((int)(TimeoutSeconds ?? 1), 1, 300),
            HistoryLimit = Math.Clamp((int)(HistoryLimit ?? 50), 50, 1000),
        };
        await _store.SaveAsync(updated);

        if (Avalonia.Application.Current is App app)
            app.ApplyTheme(IsDarkTheme);
    }
}
