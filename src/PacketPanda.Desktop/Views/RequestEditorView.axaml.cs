using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using PacketPanda.Desktop.Highlighting;
using PacketPanda.Desktop.ViewModels;
using ReactiveUI;

namespace PacketPanda.Desktop.Views;

public partial class RequestEditorView : ReactiveUserControl<RequestEditorViewModel>
{
    private CompositeDisposable? _subs;
    private DispatcherTimer? _popupCloseTimer;
    private string? _hoveredVarName;

    [GeneratedRegex(@"\{\{([^}]+)\}\}")]
    private static partial Regex VarPattern();

    public RequestEditorView()
    {
        InitializeComponent();

        UrlEditor.PointerMoved += (_, e) =>
            OnEditorPointerMoved(UrlEditor, (DataContext as RequestEditorViewModel)?.Url, e);
        UrlEditor.PointerExited += (_, _) => StartPopupCloseTimer();

        BodyEditor.PointerMoved += (_, e) =>
            OnEditorPointerMoved(BodyEditor, (DataContext as RequestEditorViewModel)?.RequestBody, e);
        BodyEditor.PointerExited += (_, _) => StartPopupCloseTimer();

        PopupValueBox.KeyDown += OnPopupValueBoxKeyDown;
        PopupEnvLink.Click += (_, _) => OnPopupEnvLinkClick();

        // PopupContent (the Border inside the PopupRoot) fires reliable pointer events;
        // VarPopup.PointerEntered/Exited are on the parent-window control and never fire for popup content.
        PopupContent.PointerEntered += (_, _) => _popupCloseTimer?.Stop();
        PopupContent.PointerPressed += (_, _) => _popupCloseTimer?.Stop();
        PopupContent.PointerExited += (_, _) => StartPopupCloseTimer();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _popupCloseTimer?.Stop();
        _popupCloseTimer = null;
        ClosePopup();

        _subs?.Dispose();
        _subs = null;

        if (DataContext is not RequestEditorViewModel vm) return;

        _subs = new CompositeDisposable();

        vm.WhenAnyValue(x => x.Url)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => VariableTextHighlighter.Apply(UrlOverlay, vm.Url, vm.GlobalsVariables, vm.ActiveEnvVariables))
            .DisposeWith(_subs);

        vm.WhenAnyValue(x => x.RequestBody)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => VariableTextHighlighter.Apply(BodyOverlay, vm.RequestBody, vm.GlobalsVariables, vm.ActiveEnvVariables))
            .DisposeWith(_subs);

        vm.WhenAnyValue(x => x.ActiveEnvironmentVariables)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                VariableTextHighlighter.Apply(UrlOverlay, vm.Url, vm.GlobalsVariables, vm.ActiveEnvVariables);
                VariableTextHighlighter.Apply(BodyOverlay, vm.RequestBody, vm.GlobalsVariables, vm.ActiveEnvVariables);
            })
            .DisposeWith(_subs);
    }

    private void OnEditorPointerMoved(TextBox editor, string? text, PointerEventArgs e)
    {
        if (DataContext is not RequestEditorViewModel vm) return;

        if (string.IsNullOrEmpty(text))
        {
            StartPopupCloseTimer();
            return;
        }

        var pos = e.GetPosition(editor);
        var charIndex = ApproximateCharIndex(pos.X, pos.Y, text, leftPadding: 9.0, topPadding: 5.0);
        var varSpan = FindVarSpanAtIndex(text, charIndex);

        if (varSpan is null)
        {
            StartPopupCloseTimer();
            return;
        }

        _popupCloseTimer?.Stop();
        if (_hoveredVarName == varSpan.Value.Name && VarPopup.IsOpen) return;
        _hoveredVarName = varSpan.Value.Name;
        PopulatePopup(varSpan.Value.Name, vm);
        if (VarPopup.IsOpen) VarPopup.IsOpen = false;  // force re-placement at new pointer position
        VarPopup.IsOpen = true;
        _popupCloseTimer?.Stop();  // popup is now open; cancel any close timer the PointerExited event may have started
    }

    private static int ApproximateCharIndex(double x, double y, string text, double leftPadding, double topPadding)
    {
        const double charWidth = 7.8;  // 13px font * ~0.6
        const double lineHeight = 15.6; // 13px font * ~1.2

        var lines = text.Split('\n');
        var lineIndex = (int)((y - topPadding) / lineHeight);
        lineIndex = Math.Clamp(lineIndex, 0, lines.Length - 1);

        var charInLine = (int)((x - leftPadding) / charWidth);
        charInLine = Math.Clamp(charInLine, 0, lines[lineIndex].Length);

        var lineStart = 0;
        for (var i = 0; i < lineIndex; i++)
            lineStart += lines[i].Length + 1;

        return lineStart + charInLine;
    }

    private static (string Name, int Start, int End)? FindVarSpanAtIndex(string text, int index)
    {
        foreach (Match m in VarPattern().Matches(text))
        {
            if (index >= m.Index && index < m.Index + m.Length)
                return (m.Groups[1].Value.Trim(), m.Index, m.Index + m.Length);
        }
        return null;
    }

    private void PopulatePopup(string varName, RequestEditorViewModel vm)
    {
        var vars = vm.ActiveEnvironmentVariables;
        PopupVarLabel.Text = $"{{{{{varName}}}}}";

        string? val = null;
        var resolved = vars is not null && vars.TryGetValue(varName, out val);
        PopupValueBox.Text = resolved ? val : string.Empty;

        var isUnresolved = !resolved;
        PopupTargetRow.IsVisible = isUnresolved;

        if (isUnresolved)
        {
            PopupTargetPicker.ItemsSource = new[] { "Active environment", "Globals" };
            PopupTargetPicker.SelectedIndex = vars is not null ? 0 : 1;
        }

        PopupHint.Text = "Enter to save · Esc to cancel";
    }

    private async void OnPopupValueBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClosePopup();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            await SavePopupAsync();
            e.Handled = true;
        }
    }

    private async Task SavePopupAsync()
    {
        if (DataContext is not RequestEditorViewModel vm) return;
        if (_hoveredVarName is null) return;

        var target = PopupTargetRow.IsVisible && PopupTargetPicker.SelectedIndex == 1
            ? VariableTarget.Globals
            : VariableTarget.ActiveEnv;

        await vm.UpdateVariableAsync(_hoveredVarName, PopupValueBox.Text ?? string.Empty, target);
        ClosePopup();
    }

    private void OnPopupEnvLinkClick()
    {
        if (DataContext is RequestEditorViewModel vm)
            vm.OpenEnvironmentEditor();
        ClosePopup();
    }

    private void StartPopupCloseTimer()
    {
        _popupCloseTimer?.Stop();
        _popupCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _popupCloseTimer.Tick += (_, _) =>
        {
            _popupCloseTimer!.Stop();
            ClosePopup();
        };
        _popupCloseTimer.Start();
    }

    private void ClosePopup()
    {
        VarPopup.IsOpen = false;
        _hoveredVarName = null;
    }
}
