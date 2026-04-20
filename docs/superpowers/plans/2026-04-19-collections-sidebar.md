# Collections Sidebar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the left-panel placeholder with a live collections tree, add a tab bar so multiple requests can be open simultaneously, and auto-reopen the last workspace on launch.

**Architecture:** Three cooperating ViewModels — `ShellViewModel` (owns tabs + sidebar), `CollectionSidebarViewModel` (folder tree + open-workspace command), and `TabViewModel` (wraps a `RequestEditorViewModel`). The current `MainWindowViewModel` is renamed `RequestEditorViewModel` and gains dirty tracking via a saved-state snapshot. Two new Core services — `IWorkspaceScanner` (recursive directory scan) and `ISettingsStore` (AppData TOML persistence) — plus two Desktop services (`IFolderPickerService`, `IDialogService`).

**Tech Stack:** C# / .NET 10, Avalonia UI 11, ReactiveUI, Tomlyn, xUnit + Moq + FluentAssertions 7.2.0.

**Spec:** `docs/superpowers/specs/2026-04-19-collections-sidebar-design.md`

**Branch:** `task/collections-sidebar` (already checked out).

---

## Workflow conventions (applies to every task)

- Run `dotnet test` before every commit. Only commit if all tests pass.
- Do **not** commit anything under `docs/superpowers/` — the spec and plan stay working-tree-only.
- Commit messages use semantic prefixes: `feat(sidebar):`, `feat(settings):`, `test(...)`, `refactor(...)`, `docs(...)`.
- Test method names use PascalCase segments — `Subject_Scenario_ExpectedOutcome` (see `CLAUDE.md`). No lowercase words between underscores.
- Assertions use FluentAssertions (`value.Should().Be(...)`). Mocks use Moq (`new Mock<IFoo>(MockBehavior.Strict)`).
- Confirm you are on `task/collections-sidebar` before starting each task: `git branch --show-current`.

---

## File map

**New in `src/PacketPanda.Core/Persistence/`:**
- `CollectionNode.cs` — `abstract record CollectionNode`, `FolderNode`, `RequestNode`
- `IWorkspaceScanner.cs` — interface
- `WorkspaceScanner.cs` — recursive directory scan implementation
- `AppSettings.cs` — `record AppSettings(string? LastWorkspacePath = null)`
- `SettingsException.cs` — non-fatal wrapper
- `ISettingsStore.cs` — interface
- `TomlSettingsStore.cs` — Tomlyn implementation, injectable settings path

**New in `src/PacketPanda.Desktop/Services/`:**
- `IFolderPickerService.cs`
- `AvaloniaFolderPickerService.cs`
- `IDialogService.cs`
- `AvaloniaDialogService.cs`

**New in `src/PacketPanda.Desktop/ViewModels/`:**
- `RequestEditorViewModel.cs` (renamed from `MainWindowViewModel.cs`, adds `IsDirty` + snapshot)
- `TabViewModel.cs`
- `CollectionNodeViewModel.cs`
- `CollectionSidebarViewModel.cs`
- `ShellViewModel.cs`

**New in `src/PacketPanda.Desktop/Views/`:**
- `RequestEditorView.axaml` + `.axaml.cs` — UserControl extracted from current center panel

**Modified:**
- `src/PacketPanda.Desktop/Views/MainWindow.axaml` — DataContext → `ShellViewModel`; left panel becomes TreeView; center panel becomes TabControl
- `src/PacketPanda.Desktop/Views/MainWindow.axaml.cs` — no logic change (still just `InitializeComponent`)
- `src/PacketPanda.Desktop/App.axaml.cs` — construct `ShellViewModel` with all deps; load settings + auto-reopen workspace

**Deleted:**
- `src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs` (replaced by `RequestEditorViewModel.cs`)
- `tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs` (replaced by `RequestEditorViewModelTests.cs`)

**New tests in `tests/PacketPanda.Tests/`:**
- `Persistence/WorkspaceScannerTests.cs`
- `Persistence/TomlSettingsStoreTests.cs`
- `ViewModels/RequestEditorViewModelTests.cs` (renamed from MainWindowViewModelTests; adds dirty-tracking tests)
- `ViewModels/TabViewModelTests.cs`
- `ViewModels/CollectionSidebarViewModelTests.cs`
- `ViewModels/ShellViewModelTests.cs`

---

## Task 1: Domain records — `CollectionNode`, `FolderNode`, `RequestNode`

**Files:**
- Create: `src/PacketPanda.Core/Persistence/CollectionNode.cs`

- [ ] **Step 1: Confirm branch**

Run: `git branch --show-current`
Expected: `task/collections-sidebar`

- [ ] **Step 2: Write the file**

```csharp
namespace PacketPanda.Core.Persistence;

public abstract record CollectionNode(string Name, string Path);

public sealed record FolderNode(string Name, string Path, IReadOnlyList<CollectionNode> Children)
    : CollectionNode(Name, Path);

public sealed record RequestNode(string Name, string Path)
    : CollectionNode(Name, Path);
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Core/Persistence/CollectionNode.cs
git commit -m "feat(sidebar): add CollectionNode domain records"
```

---

## Task 2: `IWorkspaceScanner` interface

**Files:**
- Create: `src/PacketPanda.Core/Persistence/IWorkspaceScanner.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace PacketPanda.Core.Persistence;

public interface IWorkspaceScanner
{
    Task<FolderNode> ScanAsync(string rootPath);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/PacketPanda.Core/Persistence/IWorkspaceScanner.cs
git commit -m "feat(sidebar): add IWorkspaceScanner interface"
```

---

## Task 3: `WorkspaceScanner` — failing tests first

**Files:**
- Create: `tests/PacketPanda.Tests/Persistence/WorkspaceScannerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using PacketPanda.Core.Persistence;

namespace PacketPanda.Tests.Persistence;

public class WorkspaceScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pp-scan-" + Guid.NewGuid().ToString("N"));
    private readonly IWorkspaceScanner _scanner = new WorkspaceScanner();

    public WorkspaceScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Scan_EmptyFolder_ReturnsFolderWithNoChildren()
    {
        var result = await _scanner.ScanAsync(_root);

        result.Path.Should().Be(_root);
        result.Children.Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_TomlFilesOnly_ReturnsRequestNodesAlphabetical()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "b.toml"), "");
        await File.WriteAllTextAsync(Path.Combine(_root, "a.toml"), "");

        var result = await _scanner.ScanAsync(_root);

        result.Children.Should().HaveCount(2);
        result.Children[0].Should().BeOfType<RequestNode>().Which.Name.Should().Be("a.toml");
        result.Children[1].Should().BeOfType<RequestNode>().Which.Name.Should().Be("b.toml");
    }

    [Fact]
    public async Task Scan_NonTomlFiles_AreIgnored()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "readme.md"), "");
        await File.WriteAllTextAsync(Path.Combine(_root, "req.toml"), "");

        var result = await _scanner.ScanAsync(_root);

        result.Children.Should().ContainSingle()
            .Which.Name.Should().Be("req.toml");
    }

    [Fact]
    public async Task Scan_NestedSubfolders_ProducesRecursiveTree()
    {
        var sub = Path.Combine(_root, "api");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(Path.Combine(sub, "login.toml"), "");

        var result = await _scanner.ScanAsync(_root);

        result.Children.Should().ContainSingle();
        var folder = result.Children[0].Should().BeOfType<FolderNode>().Subject;
        folder.Name.Should().Be("api");
        folder.Children.Should().ContainSingle()
            .Which.Name.Should().Be("login.toml");
    }

    [Fact]
    public async Task Scan_FoldersListedBeforeFiles_AtEachLevel()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "z.toml"), "");
        Directory.CreateDirectory(Path.Combine(_root, "a-folder"));

        var result = await _scanner.ScanAsync(_root);

        result.Children.Should().HaveCount(2);
        result.Children[0].Should().BeOfType<FolderNode>();
        result.Children[1].Should().BeOfType<RequestNode>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~WorkspaceScannerTests`
Expected: compile error — `WorkspaceScanner` does not exist.

- [ ] **Step 3: Implement `WorkspaceScanner`**

Create `src/PacketPanda.Core/Persistence/WorkspaceScanner.cs`:

```csharp
namespace PacketPanda.Core.Persistence;

public sealed class WorkspaceScanner : IWorkspaceScanner
{
    public Task<FolderNode> ScanAsync(string rootPath)
    {
        return Task.Run(() => ScanFolder(rootPath));
    }

    private static FolderNode ScanFolder(string path)
    {
        var folders = Directory.GetDirectories(path)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(ScanFolder)
            .Cast<CollectionNode>();

        var files = Directory.GetFiles(path, "*.toml")
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(p => (CollectionNode)new RequestNode(Path.GetFileName(p), p));

        var children = folders.Concat(files).ToList();
        return new FolderNode(Path.GetFileName(path), path, children);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~WorkspaceScannerTests`
Expected: 5 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/PacketPanda.Core/Persistence/WorkspaceScanner.cs tests/PacketPanda.Tests/Persistence/WorkspaceScannerTests.cs
git commit -m "feat(sidebar): add recursive WorkspaceScanner with tests"
```

---

## Task 4: `AppSettings` + `SettingsException`

**Files:**
- Create: `src/PacketPanda.Core/Persistence/AppSettings.cs`
- Create: `src/PacketPanda.Core/Persistence/SettingsException.cs`

- [ ] **Step 1: Write `AppSettings.cs`**

```csharp
namespace PacketPanda.Core.Persistence;

public sealed record AppSettings(string? LastWorkspacePath = null);
```

- [ ] **Step 2: Write `SettingsException.cs`**

```csharp
namespace PacketPanda.Core.Persistence;

public sealed class SettingsException : Exception
{
    public SettingsException(string message) : base(message) { }
    public SettingsException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Core/Persistence/AppSettings.cs src/PacketPanda.Core/Persistence/SettingsException.cs
git commit -m "feat(settings): add AppSettings record and SettingsException"
```

---

## Task 5: `ISettingsStore` interface

**Files:**
- Create: `src/PacketPanda.Core/Persistence/ISettingsStore.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace PacketPanda.Core.Persistence;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/PacketPanda.Core/Persistence/ISettingsStore.cs
git commit -m "feat(settings): add ISettingsStore interface"
```

---

## Task 6: `TomlSettingsStore` — failing tests first

**Files:**
- Create: `tests/PacketPanda.Tests/Persistence/TomlSettingsStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using PacketPanda.Core.Persistence;

namespace PacketPanda.Tests.Persistence;

public class TomlSettingsStoreTests : IDisposable
{
    private readonly string _tempPath = Path.GetTempFileName() + ".toml";

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmptyAppSettings()
    {
        File.Delete(_tempPath);
        var store = new TomlSettingsStore(_tempPath);

        var settings = await store.LoadAsync();

        settings.LastWorkspacePath.Should().BeNull();
    }

    [Fact]
    public async Task RoundTrip_PreservesLastWorkspacePath()
    {
        var store = new TomlSettingsStore(_tempPath);
        await store.SaveAsync(new AppSettings("/tmp/workspace"));

        var loaded = await store.LoadAsync();

        loaded.LastWorkspacePath.Should().Be("/tmp/workspace");
    }

    [Fact]
    public async Task Load_UnknownKey_IsIgnored()
    {
        await File.WriteAllTextAsync(_tempPath, """
            last_workspace_path = "/tmp/workspace"
            future_key = "whatever"
            """);
        var store = new TomlSettingsStore(_tempPath);

        var settings = await store.LoadAsync();

        settings.LastWorkspacePath.Should().Be("/tmp/workspace");
    }

    [Fact]
    public async Task Load_InvalidToml_ThrowsSettingsException()
    {
        await File.WriteAllTextAsync(_tempPath, "not [ valid toml !!!");
        var store = new TomlSettingsStore(_tempPath);

        var act = async () => await store.LoadAsync();

        await act.Should().ThrowAsync<SettingsException>();
    }

    [Fact]
    public async Task Save_CreatesParentDirectoryIfMissing()
    {
        var nested = Path.Combine(Path.GetTempPath(), "pp-settings-" + Guid.NewGuid().ToString("N"), "settings.toml");
        try
        {
            var store = new TomlSettingsStore(nested);
            await store.SaveAsync(new AppSettings("/tmp/x"));

            File.Exists(nested).Should().BeTrue();
        }
        finally
        {
            var dir = Path.GetDirectoryName(nested)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TomlSettingsStoreTests`
Expected: compile error — `TomlSettingsStore` does not exist.

- [ ] **Step 3: Implement `TomlSettingsStore`**

Create `src/PacketPanda.Core/Persistence/TomlSettingsStore.cs`:

```csharp
using Tomlyn;
using Tomlyn.Model;

namespace PacketPanda.Core.Persistence;

public sealed class TomlSettingsStore : ISettingsStore
{
    private readonly string _path;

    public TomlSettingsStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PacketPanda",
        "settings.toml");

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_path))
            return new AppSettings();

        string toml;
        try
        {
            toml = await File.ReadAllTextAsync(_path);
        }
        catch (Exception ex)
        {
            throw new SettingsException($"Could not read settings: {ex.Message}", ex);
        }

        TomlTable table;
        try
        {
            table = Toml.ToModel(toml);
        }
        catch (Exception ex)
        {
            throw new SettingsException($"Invalid settings TOML: {ex.Message}", ex);
        }

        string? workspace = null;
        if (table.TryGetValue("last_workspace_path", out var v) && v is string s && s.Length > 0)
            workspace = s;

        return new AppSettings(workspace);
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var table = new TomlTable();
        if (!string.IsNullOrEmpty(settings.LastWorkspacePath))
            table["last_workspace_path"] = settings.LastWorkspacePath;

        var toml = Toml.FromModel(table);
        try
        {
            await File.WriteAllTextAsync(_path, toml);
        }
        catch (Exception ex)
        {
            throw new SettingsException($"Could not write settings: {ex.Message}", ex);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~TomlSettingsStoreTests`
Expected: 5 tests passing.

- [ ] **Step 5: Commit**

```bash
git add src/PacketPanda.Core/Persistence/TomlSettingsStore.cs tests/PacketPanda.Tests/Persistence/TomlSettingsStoreTests.cs
git commit -m "feat(settings): add TomlSettingsStore with AppData persistence"
```

---

## Task 7: `IFolderPickerService` + `AvaloniaFolderPickerService`

**Files:**
- Create: `src/PacketPanda.Desktop/Services/IFolderPickerService.cs`
- Create: `src/PacketPanda.Desktop/Services/AvaloniaFolderPickerService.cs`

- [ ] **Step 1: Write `IFolderPickerService.cs`**

```csharp
namespace PacketPanda.Desktop.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync();
}
```

- [ ] **Step 2: Write `AvaloniaFolderPickerService.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PacketPanda.Desktop.Services;

public sealed class AvaloniaFolderPickerService(Window window) : IFolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Workspace Folder",
            AllowMultiple = false
        });

        return folders.Count == 1 ? folders[0].Path.LocalPath : null;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Desktop/Services/IFolderPickerService.cs src/PacketPanda.Desktop/Services/AvaloniaFolderPickerService.cs
git commit -m "feat(sidebar): add folder picker service"
```

---

## Task 8: `IDialogService` + `AvaloniaDialogService`

The spec calls for "Unsaved changes. Discard?" confirmation. Avalonia has no built-in MessageBox; we'll implement a minimal modal `Window` so we don't pull in extra dependencies. The service lives behind an interface so the ShellViewModel tests can Moq it.

**Files:**
- Create: `src/PacketPanda.Desktop/Services/IDialogService.cs`
- Create: `src/PacketPanda.Desktop/Services/AvaloniaDialogService.cs`

- [ ] **Step 1: Write `IDialogService.cs`**

```csharp
namespace PacketPanda.Desktop.Services;

public interface IDialogService
{
    Task<bool> ConfirmDiscardAsync(string tabTitle);
}
```

- [ ] **Step 2: Write `AvaloniaDialogService.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PacketPanda.Desktop.Services;

public sealed class AvaloniaDialogService(Window owner) : IDialogService
{
    public async Task<bool> ConfirmDiscardAsync(string tabTitle)
    {
        var result = false;
        var dialog = new Window
        {
            Title = "Unsaved changes",
            Width = 360,
            Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var message = new TextBlock
        {
            Text = $"'{tabTitle}' has unsaved changes. Discard?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(16, 16, 16, 8)
        };

        var discard = new Button { Content = "Discard", Margin = new Avalonia.Thickness(4) };
        var cancel = new Button { Content = "Cancel", Margin = new Avalonia.Thickness(4), IsDefault = true };

        discard.Click += (_, _) => { result = true; dialog.Close(); };
        cancel.Click += (_, _) => { result = false; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(8)
        };
        buttons.Children.Add(discard);
        buttons.Children.Add(cancel);

        var layout = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(message);

        dialog.Content = layout;

        await dialog.ShowDialog(owner);
        return result;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Desktop/Services/IDialogService.cs src/PacketPanda.Desktop/Services/AvaloniaDialogService.cs
git commit -m "feat(sidebar): add dialog service for discard confirmation"
```

---

## Task 9: Rename `MainWindowViewModel` → `RequestEditorViewModel`, add dirty tracking

This is the biggest single task — a rename, plus adding `IsDirty` state driven by a saved snapshot. We do it in two commits: (a) pure rename first so history stays clean, (b) add `IsDirty` behaviour with tests.

**Files:**
- Delete: `src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs`
- Delete: `tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs`
- Create: `src/PacketPanda.Desktop/ViewModels/RequestEditorViewModel.cs`
- Create: `tests/PacketPanda.Tests/ViewModels/RequestEditorViewModelTests.cs`

- [ ] **Step 1: Rename the VM file and class (no behaviour change)**

Copy `src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs` to `src/PacketPanda.Desktop/ViewModels/RequestEditorViewModel.cs` and:
- replace the class name `MainWindowViewModel` → `RequestEditorViewModel`
- keep all other code identical for now

Full content (post-rename, behaviour unchanged):

```csharp
using System.Reactive;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using ReactiveUI;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Desktop.ViewModels;

public class RequestEditorViewModel : ViewModelBase
{
    private readonly IHttpRequestSender _sender;
    private readonly IRequestFileStore _store;
    private readonly IFilePickerService _picker;

    private HttpMethod _selectedMethod = HttpMethod.Get;
    private string _url = string.Empty;
    private string? _requestBody;
    private int? _statusCode;
    private long? _elapsedMilliseconds;
    private string? _responseBody;
    private string? _errorMessage;
    private string? _currentFilePath;

    public RequestEditorViewModel(IHttpRequestSender sender, IRequestFileStore store, IFilePickerService picker)
    {
        _sender = sender;
        _store = store;
        _picker = picker;

        var canSend = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));
        var canSave = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));

        SendCommand = ReactiveCommand.CreateFromTask(SendAsync, canSend);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync, canSave);
    }

    public HttpMethod SelectedMethod
    {
        get => _selectedMethod;
        set => this.RaiseAndSetIfChanged(ref _selectedMethod, value);
    }

    public string Url
    {
        get => _url;
        set => this.RaiseAndSetIfChanged(ref _url, value);
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

    public ReactiveCommand<Unit, Unit> SendCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }

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

        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        var request = new HttpRequest(SelectedMethod, Url, body);
        var result = await _sender.SendAsync(request);

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
                StatusCode = null;
                ResponseBody = null;
                ElapsedMilliseconds = null;
                break;
        }
    }

    private async Task OpenAsync()
    {
        var path = await _picker.PickOpenAsync();
        if (path is null) return;

        try
        {
            var request = await _store.LoadAsync(path);
            SelectedMethod = request.Method;
            Url = request.Url;
            RequestBody = request.Body;
            StatusCode = null;
            ResponseBody = null;
            ElapsedMilliseconds = null;
            ErrorMessage = null;
            CurrentFilePath = path;
        }
        catch (RequestFileException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task SaveAsync()
    {
        if (CurrentFilePath is null)
        {
            await SaveAsAsync();
            return;
        }

        await SaveToPathAsync(CurrentFilePath);
    }

    private async Task SaveAsAsync()
    {
        var path = await _picker.PickSaveAsync();
        if (path is null) return;

        var succeeded = await SaveToPathAsync(path);
        if (succeeded)
            CurrentFilePath = path;
    }

    private async Task<bool> SaveToPathAsync(string path)
    {
        try
        {
            var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
            var request = new HttpRequest(SelectedMethod, Url, body);
            await _store.SaveAsync(path, request);
            ErrorMessage = null;
            return true;
        }
        catch (RequestFileException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }
}
```

Delete `src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs`:

```bash
git rm src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs
```

- [ ] **Step 2: Rename the tests file**

Copy `tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs` to `tests/PacketPanda.Tests/ViewModels/RequestEditorViewModelTests.cs` and replace all occurrences of `MainWindowViewModel` with `RequestEditorViewModel` (class name + `CreateVm` return type + constructor call).

```bash
git rm tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs
```

- [ ] **Step 3: Update `App.axaml.cs` to use the new name**

In `src/PacketPanda.Desktop/App.axaml.cs` line 32, replace `new MainWindowViewModel(...)` with `new RequestEditorViewModel(...)`. (This is a temporary change — it gets replaced again in Task 16, but we need the build green now.)

Edit line 32 from:
```csharp
mainWindow.DataContext = new MainWindowViewModel(sender, store, picker);
```
to:
```csharp
mainWindow.DataContext = new RequestEditorViewModel(sender, store, picker);
```

- [ ] **Step 4: Update `MainWindow.axaml` x:DataType**

In `src/PacketPanda.Desktop/Views/MainWindow.axaml`, change line 9:
```xml
x:DataType="vm:MainWindowViewModel"
```
to:
```xml
x:DataType="vm:RequestEditorViewModel"
```

- [ ] **Step 5: Build + run all tests**

Run: `dotnet test`
Expected: all tests pass (including the renamed `RequestEditorViewModelTests`).

- [ ] **Step 6: Commit the rename**

```bash
git add -A
git commit -m "refactor(vm): rename MainWindowViewModel to RequestEditorViewModel"
```

- [ ] **Step 7: Add failing dirty-tracking tests**

Append to `tests/PacketPanda.Tests/ViewModels/RequestEditorViewModelTests.cs` (before the closing brace):

```csharp
    [Fact]
    public void IsDirty_FreshViewModel_IsFalse()
    {
        var vm = CreateVm();

        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void IsDirty_AfterEditingUrl_IsTrue()
    {
        var vm = CreateVm();

        vm.Url = "https://example.com";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_AfterEditingBody_IsTrue()
    {
        var vm = CreateVm();

        vm.RequestBody = "{}";

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_AfterEditingMethod_IsTrue()
    {
        var vm = CreateVm();

        vm.SelectedMethod = HttpMethod.Post;

        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task IsDirty_IsFalseAfterSuccessfulOpen()
    {
        _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync("/tmp/req.toml");
        _store.Setup(s => s.LoadAsync("/tmp/req.toml"))
              .ReturnsAsync(new HttpRequest(HttpMethod.Get, "https://example.com", null));

        var vm = CreateVm();
        vm.Url = "dirty";

        await vm.OpenCommand.Execute();

        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task IsDirty_IsFalseAfterSuccessfulSave()
    {
        _store.Setup(s => s.SaveAsync("/tmp/req.toml", It.IsAny<HttpRequest>()))
              .Returns(Task.CompletedTask);

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.CurrentFilePath = "/tmp/req.toml";

        await vm.SaveCommand.Execute();

        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task IsDirty_StaysTrueAfterFailedSave()
    {
        _store.Setup(s => s.SaveAsync("/tmp/req.toml", It.IsAny<HttpRequest>()))
              .ThrowsAsync(new RequestFileException("disk full"));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.CurrentFilePath = "/tmp/req.toml";

        await vm.SaveCommand.Execute();

        vm.IsDirty.Should().BeTrue();
    }
```

- [ ] **Step 8: Run tests — they should fail**

Run: `dotnet test --filter FullyQualifiedName~RequestEditorViewModelTests`
Expected: compile error — `IsDirty` does not exist.

- [ ] **Step 9: Add `IsDirty` + snapshot to `RequestEditorViewModel`**

Edit `src/PacketPanda.Desktop/ViewModels/RequestEditorViewModel.cs`:

Add field (next to the other backing fields, near line 23):

```csharp
    private bool _isDirty;
    private RequestSnapshot? _savedSnapshot;
```

Add a nested snapshot record **inside the class**, right after the field declarations:

```csharp
    private sealed record RequestSnapshot(HttpMethod Method, string Url, string? Body);
```

Add the `IsDirty` public property (after `CurrentFilePath`):

```csharp
    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }
```

In the constructor, after the command assignments, subscribe to state changes to recompute `IsDirty`:

```csharp
        this.WhenAnyValue(
                vm => vm.SelectedMethod,
                vm => vm.Url,
                vm => vm.RequestBody)
            .Subscribe(_ => RecomputeDirty());
```

Add (at the bottom of the class):

```csharp
    private void RecomputeDirty()
    {
        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        var current = new RequestSnapshot(SelectedMethod, Url, body);
        IsDirty = _savedSnapshot != current;
    }

    private void CaptureSnapshot()
    {
        var body = string.IsNullOrEmpty(RequestBody) ? null : RequestBody;
        _savedSnapshot = new RequestSnapshot(SelectedMethod, Url, body);
        IsDirty = false;
    }
```

Initial snapshot: a fresh VM has `_url = ""`, `_selectedMethod = Get`, `_requestBody = null`. The spec says blank new tab: snapshot null, `IsDirty = false` until user types. So in the constructor, at the very end, add:

```csharp
        CaptureSnapshot();
```

In `OpenAsync`, after the successful state assignment (after `CurrentFilePath = path;`), call:

```csharp
            CaptureSnapshot();
```

In `SaveToPathAsync`, inside the `try` block after `await _store.SaveAsync(...)` and before returning `true`:

```csharp
            CaptureSnapshot();
```

Also add the `using System` (if not already imported via global usings) for `IObservable` — ReactiveUI extensions cover this; no extra using needed.

- [ ] **Step 10: Run tests**

Run: `dotnet test`
Expected: all tests pass, including the new `IsDirty_*` tests.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat(sidebar): add IsDirty tracking to RequestEditorViewModel"
```

---

## Task 10: `CollectionNodeViewModel`

**Files:**
- Create: `src/PacketPanda.Desktop/ViewModels/CollectionNodeViewModel.cs`

This VM is a dumb data holder — no tests needed beyond what `CollectionSidebarViewModelTests` already covers transitively.

- [ ] **Step 1: Write the file**

```csharp
using System.Collections.ObjectModel;
using PacketPanda.Core.Persistence;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class CollectionNodeViewModel : ReactiveObject
{
    private bool _isExpanded;

    public CollectionNodeViewModel(string name, string path, bool isFolder, IEnumerable<CollectionNodeViewModel> children)
    {
        Name = name;
        Path = path;
        IsFolder = isFolder;
        Children = new ObservableCollection<CollectionNodeViewModel>(children);
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsFolder { get; }
    public ObservableCollection<CollectionNodeViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public static CollectionNodeViewModel From(CollectionNode node)
    {
        return node switch
        {
            FolderNode f => new CollectionNodeViewModel(f.Name, f.Path, isFolder: true, f.Children.Select(From)),
            RequestNode r => new CollectionNodeViewModel(r.Name, r.Path, isFolder: false, []),
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}")
        };
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/PacketPanda.Desktop/ViewModels/CollectionNodeViewModel.cs
git commit -m "feat(sidebar): add CollectionNodeViewModel"
```

---

## Task 11: `CollectionSidebarViewModel` — failing tests first

**Files:**
- Create: `tests/PacketPanda.Tests/ViewModels/CollectionSidebarViewModelTests.cs`
- Create: `src/PacketPanda.Desktop/ViewModels/CollectionSidebarViewModel.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Tests.ViewModels;

public class CollectionSidebarViewModelTests
{
    private readonly Mock<IFolderPickerService> _picker = new(MockBehavior.Strict);
    private readonly Mock<IWorkspaceScanner> _scanner = new(MockBehavior.Strict);
    private readonly Mock<ISettingsStore> _settings = new(MockBehavior.Strict);

    private CollectionSidebarViewModel CreateVm() =>
        new(_picker.Object, _scanner.Object, _settings.Object);

    [Fact]
    public async Task OpenWorkspaceCommand_Cancelled_LeavesStateUnchanged()
    {
        _picker.Setup(p => p.PickFolderAsync()).ReturnsAsync((string?)null);
        var vm = CreateVm();

        await vm.OpenWorkspaceCommand.Execute();

        vm.RootNodes.Should().BeEmpty();
        vm.WorkspacePath.Should().BeNull();
        _scanner.Verify(s => s.ScanAsync(It.IsAny<string>()), Times.Never);
        _settings.Verify(s => s.SaveAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task OpenWorkspaceCommand_Success_PopulatesRootNodesAndSavesSettings()
    {
        var tree = new FolderNode("root", "/ws", new CollectionNode[]
        {
            new RequestNode("a.toml", "/ws/a.toml")
        });
        _picker.Setup(p => p.PickFolderAsync()).ReturnsAsync("/ws");
        _scanner.Setup(s => s.ScanAsync("/ws")).ReturnsAsync(tree);
        _settings.Setup(s => s.SaveAsync(It.Is<AppSettings>(a => a.LastWorkspacePath == "/ws")))
                 .Returns(Task.CompletedTask);

        var vm = CreateVm();

        await vm.OpenWorkspaceCommand.Execute();

        vm.WorkspacePath.Should().Be("/ws");
        vm.RootNodes.Should().ContainSingle()
            .Which.Name.Should().Be("a.toml");
        _settings.VerifyAll();
    }

    [Fact]
    public async Task OpenWorkspaceCommand_ScanFailure_LeavesRootNodesEmptyAndSetsError()
    {
        _picker.Setup(p => p.PickFolderAsync()).ReturnsAsync("/bad");
        _scanner.Setup(s => s.ScanAsync("/bad"))
                .ThrowsAsync(new DirectoryNotFoundException("not there"));

        var vm = CreateVm();

        await vm.OpenWorkspaceCommand.Execute();

        vm.RootNodes.Should().BeEmpty();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        _settings.Verify(s => s.SaveAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task LoadWorkspace_PopulatesRootNodesWithoutPickerOrSettings()
    {
        var tree = new FolderNode("root", "/ws", new CollectionNode[]
        {
            new RequestNode("a.toml", "/ws/a.toml")
        });
        _scanner.Setup(s => s.ScanAsync("/ws")).ReturnsAsync(tree);

        var vm = CreateVm();

        await vm.LoadWorkspaceAsync("/ws");

        vm.WorkspacePath.Should().Be("/ws");
        vm.RootNodes.Should().ContainSingle();
        _picker.Verify(p => p.PickFolderAsync(), Times.Never);
        _settings.Verify(s => s.SaveAsync(It.IsAny<AppSettings>()), Times.Never);
    }

    [Fact]
    public async Task RequestOpened_FiresWhenRequestNodeActivated()
    {
        var vm = CreateVm();
        var node = new CollectionNodeViewModel("a.toml", "/ws/a.toml", isFolder: false, []);

        string? received = null;
        using var _ = vm.RequestOpened.Subscribe(p => received = p);

        await vm.OpenRequestCommand.Execute(node);

        received.Should().Be("/ws/a.toml");
    }

    [Fact]
    public async Task RequestOpened_DoesNotFireForFolderNodes()
    {
        var vm = CreateVm();
        var folder = new CollectionNodeViewModel("api", "/ws/api", isFolder: true, []);

        string? received = null;
        using var _ = vm.RequestOpened.Subscribe(p => received = p);

        await vm.OpenRequestCommand.Execute(folder);

        received.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~CollectionSidebarViewModelTests`
Expected: compile error — `CollectionSidebarViewModel` does not exist.

- [ ] **Step 3: Implement `CollectionSidebarViewModel`**

Create `src/PacketPanda.Desktop/ViewModels/CollectionSidebarViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class CollectionSidebarViewModel : ReactiveObject
{
    private readonly IFolderPickerService _picker;
    private readonly IWorkspaceScanner _scanner;
    private readonly ISettingsStore _settings;
    private readonly Subject<string> _requestOpened = new();

    private string? _workspacePath;
    private string? _errorMessage;

    public CollectionSidebarViewModel(
        IFolderPickerService picker,
        IWorkspaceScanner scanner,
        ISettingsStore settings)
    {
        _picker = picker;
        _scanner = scanner;
        _settings = settings;

        RootNodes = new ObservableCollection<CollectionNodeViewModel>();
        OpenWorkspaceCommand = ReactiveCommand.CreateFromTask(OpenWorkspaceAsync);
        OpenRequestCommand = ReactiveCommand.CreateFromTask<CollectionNodeViewModel>(OpenRequestAsync);
    }

    public ObservableCollection<CollectionNodeViewModel> RootNodes { get; }

    public string? WorkspacePath
    {
        get => _workspacePath;
        private set => this.RaiseAndSetIfChanged(ref _workspacePath, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ReactiveCommand<Unit, Unit> OpenWorkspaceCommand { get; }
    public ReactiveCommand<CollectionNodeViewModel, Unit> OpenRequestCommand { get; }

    public IObservable<string> RequestOpened => _requestOpened;

    public async Task LoadWorkspaceAsync(string path)
    {
        ErrorMessage = null;
        try
        {
            var tree = await _scanner.ScanAsync(path);
            ReplaceRoots(tree);
            WorkspacePath = path;
        }
        catch (Exception ex)
        {
            RootNodes.Clear();
            WorkspacePath = null;
            ErrorMessage = $"Could not open workspace: {ex.Message}";
        }
    }

    private async Task OpenWorkspaceAsync()
    {
        var path = await _picker.PickFolderAsync();
        if (path is null) return;

        ErrorMessage = null;
        try
        {
            var tree = await _scanner.ScanAsync(path);
            ReplaceRoots(tree);
            WorkspacePath = path;
            await _settings.SaveAsync(new AppSettings(path));
        }
        catch (Exception ex)
        {
            RootNodes.Clear();
            WorkspacePath = null;
            ErrorMessage = $"Could not open workspace: {ex.Message}";
        }
    }

    private Task OpenRequestAsync(CollectionNodeViewModel node)
    {
        if (!node.IsFolder)
            _requestOpened.OnNext(node.Path);

        return Task.CompletedTask;
    }

    private void ReplaceRoots(FolderNode tree)
    {
        RootNodes.Clear();
        foreach (var child in tree.Children)
            RootNodes.Add(CollectionNodeViewModel.From(child));
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter FullyQualifiedName~CollectionSidebarViewModelTests`
Expected: 6 tests passing.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(sidebar): add CollectionSidebarViewModel with open/scan/request events"
```

---

## Task 12: `TabViewModel` — failing tests first

`TabViewModel` wraps a `RequestEditorViewModel` and forwards title/dirty/path. The close command consults `IDialogService` only when dirty.

**Files:**
- Create: `tests/PacketPanda.Tests/ViewModels/TabViewModelTests.cs`
- Create: `src/PacketPanda.Desktop/ViewModels/TabViewModel.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Tests.ViewModels;

public class TabViewModelTests
{
    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);
    private readonly Mock<IRequestFileStore> _store = new(MockBehavior.Strict);
    private readonly Mock<IFilePickerService> _picker = new(MockBehavior.Strict);
    private readonly Mock<IDialogService> _dialog = new(MockBehavior.Strict);

    private RequestEditorViewModel NewEditor() =>
        new(_sender.Object, _store.Object, _picker.Object);

    [Fact]
    public void Title_ForFreshTab_IsUntitled()
    {
        var tab = new TabViewModel(NewEditor(), _dialog.Object);

        tab.Title.Should().Be("Untitled");
    }

    [Fact]
    public void Title_WhenEditorHasFilePath_UsesFileNameWithoutExtension()
    {
        var editor = NewEditor();
        editor.CurrentFilePath = "/tmp/login.toml";

        var tab = new TabViewModel(editor, _dialog.Object);

        tab.Title.Should().Be("login");
    }

    [Fact]
    public void Title_WhenDirty_IncludesDirtyIndicator()
    {
        var editor = NewEditor();
        editor.CurrentFilePath = "/tmp/login.toml";
        editor.Url = "https://example.com"; // makes it dirty

        var tab = new TabViewModel(editor, _dialog.Object);

        tab.Title.Should().Be("login ●");
    }

    [Fact]
    public void FilePath_ForwardsFromEditor()
    {
        var editor = NewEditor();
        editor.CurrentFilePath = "/tmp/x.toml";

        var tab = new TabViewModel(editor, _dialog.Object);

        tab.FilePath.Should().Be("/tmp/x.toml");
    }

    [Fact]
    public void IsDirty_ForwardsFromEditor()
    {
        var editor = NewEditor();
        editor.Url = "https://example.com";

        var tab = new TabViewModel(editor, _dialog.Object);

        tab.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task CloseCommand_WhenClean_DoesNotPromptAndSignalsClose()
    {
        var tab = new TabViewModel(NewEditor(), _dialog.Object);
        var observed = false;
        using var _ = tab.CloseRequested.Subscribe(_ => observed = true);

        await tab.CloseCommand.Execute();

        observed.Should().BeTrue();
        _dialog.Verify(d => d.ConfirmDiscardAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CloseCommand_WhenDirtyAndUserConfirms_SignalsClose()
    {
        var editor = NewEditor();
        editor.Url = "https://example.com";
        _dialog.Setup(d => d.ConfirmDiscardAsync(It.IsAny<string>())).ReturnsAsync(true);

        var tab = new TabViewModel(editor, _dialog.Object);
        var observed = false;
        using var _ = tab.CloseRequested.Subscribe(_ => observed = true);

        await tab.CloseCommand.Execute();

        observed.Should().BeTrue();
    }

    [Fact]
    public async Task CloseCommand_WhenDirtyAndUserCancels_DoesNotSignalClose()
    {
        var editor = NewEditor();
        editor.Url = "https://example.com";
        _dialog.Setup(d => d.ConfirmDiscardAsync(It.IsAny<string>())).ReturnsAsync(false);

        var tab = new TabViewModel(editor, _dialog.Object);
        var observed = false;
        using var _ = tab.CloseRequested.Subscribe(_ => observed = true);

        await tab.CloseCommand.Execute();

        observed.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TabViewModelTests`
Expected: compile error — `TabViewModel` does not exist.

- [ ] **Step 3: Implement `TabViewModel`**

Create `src/PacketPanda.Desktop/ViewModels/TabViewModel.cs`:

```csharp
using System.Reactive;
using System.Reactive.Subjects;
using PacketPanda.Desktop.Services;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class TabViewModel : ReactiveObject
{
    private readonly IDialogService _dialog;
    private readonly Subject<Unit> _closeRequested = new();

    public TabViewModel(RequestEditorViewModel editor, IDialogService dialog)
    {
        Editor = editor;
        _dialog = dialog;

        this.WhenAnyValue(t => t.Editor.CurrentFilePath, t => t.Editor.IsDirty)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(FilePath));
                this.RaisePropertyChanged(nameof(IsDirty));
                this.RaisePropertyChanged(nameof(Title));
            });

        CloseCommand = ReactiveCommand.CreateFromTask(CloseAsync);
    }

    public RequestEditorViewModel Editor { get; }

    public string? FilePath => Editor.CurrentFilePath;

    public bool IsDirty => Editor.IsDirty;

    public string Title
    {
        get
        {
            var name = FilePath is null
                ? "Untitled"
                : Path.GetFileNameWithoutExtension(FilePath);
            return IsDirty ? $"{name} ●" : name;
        }
    }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public IObservable<Unit> CloseRequested => _closeRequested;

    private async Task CloseAsync()
    {
        if (IsDirty)
        {
            var discard = await _dialog.ConfirmDiscardAsync(Title);
            if (!discard) return;
        }

        _closeRequested.OnNext(Unit.Default);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter FullyQualifiedName~TabViewModelTests`
Expected: 8 tests passing.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(sidebar): add TabViewModel with close confirmation"
```

---

## Task 13: `ShellViewModel` — failing tests first

`ShellViewModel` owns the tab list, active tab, and reacts to the sidebar's `RequestOpened` observable. It constructs new `TabViewModel` instances via an injected factory (so tests don't need real editors).

**Files:**
- Create: `tests/PacketPanda.Tests/ViewModels/ShellViewModelTests.cs`
- Create: `src/PacketPanda.Desktop/ViewModels/ShellViewModel.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.ViewModels;

public class ShellViewModelTests
{
    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);
    private readonly Mock<IRequestFileStore> _store = new(MockBehavior.Strict);
    private readonly Mock<IFilePickerService> _picker = new(MockBehavior.Strict);
    private readonly Mock<IDialogService> _dialog = new(MockBehavior.Strict);

    private readonly Mock<IFolderPickerService> _folderPicker = new(MockBehavior.Strict);
    private readonly Mock<IWorkspaceScanner> _scanner = new(MockBehavior.Strict);
    private readonly Mock<ISettingsStore> _settings = new(MockBehavior.Strict);

    private CollectionSidebarViewModel NewSidebar() =>
        new(_folderPicker.Object, _scanner.Object, _settings.Object);

    private RequestEditorViewModel NewEditor() =>
        new(_sender.Object, _store.Object, _picker.Object);

    private ShellViewModel CreateShell(CollectionSidebarViewModel? sidebar = null) =>
        new(sidebar ?? NewSidebar(), NewEditor, _dialog.Object, _store.Object);

    [Fact]
    public async Task NewTabCommand_AddsUntitledTabAndSetsItActive()
    {
        var shell = CreateShell();

        await shell.NewTabCommand.Execute();

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab.Should().Be(shell.Tabs[0]);
        shell.ActiveTab!.Title.Should().Be("Untitled");
    }

    [Fact]
    public async Task OpenFileInTab_WithNewPath_LoadsFileAndAddsTab()
    {
        _store.Setup(s => s.LoadAsync("/tmp/a.toml"))
              .ReturnsAsync(new HttpRequest(HttpMethod.Get, "https://example.com", null));

        var shell = CreateShell();

        await shell.OpenFileInTabCommand.Execute("/tmp/a.toml");

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.FilePath.Should().Be("/tmp/a.toml");
        shell.ActiveTab.Editor.Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task OpenFileInTab_WithAlreadyOpenPath_FocusesExistingTab()
    {
        _store.Setup(s => s.LoadAsync("/tmp/a.toml"))
              .ReturnsAsync(new HttpRequest(HttpMethod.Get, "https://example.com", null));

        var shell = CreateShell();
        await shell.OpenFileInTabCommand.Execute("/tmp/a.toml");
        await shell.NewTabCommand.Execute(); // active tab is now the blank one

        await shell.OpenFileInTabCommand.Execute("/tmp/a.toml");

        shell.Tabs.Should().HaveCount(2);
        shell.ActiveTab!.FilePath.Should().Be("/tmp/a.toml");
        _store.Verify(s => s.LoadAsync("/tmp/a.toml"), Times.Once);
    }

    [Fact]
    public async Task OpenFileInTab_LoadFailure_AddsTabWithErrorMessage()
    {
        _store.Setup(s => s.LoadAsync("/tmp/bad.toml"))
              .ThrowsAsync(new RequestFileException("Invalid TOML: bad"));

        var shell = CreateShell();

        await shell.OpenFileInTabCommand.Execute("/tmp/bad.toml");

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.Editor.ErrorMessage.Should().Be("Invalid TOML: bad");
    }

    [Fact]
    public async Task SidebarRequestOpened_TriggersOpenFileInTab()
    {
        _store.Setup(s => s.LoadAsync("/ws/x.toml"))
              .ReturnsAsync(new HttpRequest(HttpMethod.Get, "https://x.com", null));

        var sidebar = NewSidebar();
        var shell = CreateShell(sidebar);
        var node = new CollectionNodeViewModel("x.toml", "/ws/x.toml", isFolder: false, []);

        await sidebar.OpenRequestCommand.Execute(node);

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab!.FilePath.Should().Be("/ws/x.toml");
    }

    [Fact]
    public async Task CloseTab_CleanTab_RemovesTabAndActivatesAdjacent()
    {
        var shell = CreateShell();
        await shell.NewTabCommand.Execute();
        await shell.NewTabCommand.Execute();
        var first = shell.Tabs[0];
        shell.ActiveTab = first;

        await first.CloseCommand.Execute();

        shell.Tabs.Should().ContainSingle();
        shell.ActiveTab.Should().Be(shell.Tabs[0]);
    }

    [Fact]
    public async Task CloseTab_LastTab_LeavesActiveTabNull()
    {
        var shell = CreateShell();
        await shell.NewTabCommand.Execute();

        await shell.Tabs[0].CloseCommand.Execute();

        shell.Tabs.Should().BeEmpty();
        shell.ActiveTab.Should().BeNull();
    }

    [Fact]
    public async Task CloseTab_DirtyTabUserConfirms_RemovesTab()
    {
        _dialog.Setup(d => d.ConfirmDiscardAsync(It.IsAny<string>())).ReturnsAsync(true);

        var shell = CreateShell();
        await shell.NewTabCommand.Execute();
        shell.Tabs[0].Editor.Url = "https://dirty.com";

        await shell.Tabs[0].CloseCommand.Execute();

        shell.Tabs.Should().BeEmpty();
    }

    [Fact]
    public async Task CloseTab_DirtyTabUserCancels_DoesNotRemoveTab()
    {
        _dialog.Setup(d => d.ConfirmDiscardAsync(It.IsAny<string>())).ReturnsAsync(false);

        var shell = CreateShell();
        await shell.NewTabCommand.Execute();
        shell.Tabs[0].Editor.Url = "https://dirty.com";

        await shell.Tabs[0].CloseCommand.Execute();

        shell.Tabs.Should().ContainSingle();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ShellViewModelTests`
Expected: compile error — `ShellViewModel` does not exist.

- [ ] **Step 3: Implement `ShellViewModel`**

Create `src/PacketPanda.Desktop/ViewModels/ShellViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Reactive;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class ShellViewModel : ViewModelBase
{
    private readonly Func<RequestEditorViewModel> _editorFactory;
    private readonly IDialogService _dialog;
    private readonly IRequestFileStore _store;

    private TabViewModel? _activeTab;

    public ShellViewModel(
        CollectionSidebarViewModel sidebar,
        Func<RequestEditorViewModel> editorFactory,
        IDialogService dialog,
        IRequestFileStore store)
    {
        Sidebar = sidebar;
        _editorFactory = editorFactory;
        _dialog = dialog;
        _store = store;

        Tabs = new ObservableCollection<TabViewModel>();

        NewTabCommand = ReactiveCommand.Create(AddBlankTab);
        OpenFileInTabCommand = ReactiveCommand.CreateFromTask<string>(OpenFileInTabAsync);

        Sidebar.RequestOpened.Subscribe(path => OpenFileInTabCommand.Execute(path).Subscribe());
    }

    public CollectionSidebarViewModel Sidebar { get; }
    public ObservableCollection<TabViewModel> Tabs { get; }

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        set => this.RaiseAndSetIfChanged(ref _activeTab, value);
    }

    public ReactiveCommand<Unit, Unit> NewTabCommand { get; }
    public ReactiveCommand<string, Unit> OpenFileInTabCommand { get; }

    private void AddBlankTab()
    {
        var editor = _editorFactory();
        var tab = new TabViewModel(editor, _dialog);
        tab.CloseRequested.Subscribe(_ => CloseTab(tab));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    private async Task OpenFileInTabAsync(string path)
    {
        var existing = Tabs.FirstOrDefault(t => t.FilePath == path);
        if (existing is not null)
        {
            ActiveTab = existing;
            return;
        }

        var editor = _editorFactory();
        try
        {
            var request = await _store.LoadAsync(path);
            editor.SelectedMethod = request.Method;
            editor.Url = request.Url;
            editor.RequestBody = request.Body;
            editor.CurrentFilePath = path;
        }
        catch (RequestFileException ex)
        {
            editor.ErrorMessage = ex.Message;
        }

        var tab = new TabViewModel(editor, _dialog);
        tab.CloseRequested.Subscribe(_ => CloseTab(tab));
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    private void CloseTab(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        var wasActive = ReferenceEquals(ActiveTab, tab);
        Tabs.RemoveAt(index);

        if (!wasActive) return;

        if (Tabs.Count == 0)
            ActiveTab = null;
        else
            ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }
}
```

Note: when `editor.Url` is set while loading, `RequestEditorViewModel.CaptureSnapshot()` was called in its constructor with empty state. Setting new state here would normally mark it dirty. Because this bypasses the VM's own Open command, we need to re-capture the snapshot after populating the fields. Append this to `OpenFileInTabAsync`, inside the `try` block, after `editor.CurrentFilePath = path;`:

```csharp
            editor.MarkSaved();
```

And add to `RequestEditorViewModel` (public method):

```csharp
    public void MarkSaved()
    {
        CaptureSnapshot();
    }
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter FullyQualifiedName~ShellViewModelTests`
Expected: 9 tests passing.

- [ ] **Step 5: Run full suite**

Run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(sidebar): add ShellViewModel orchestrating tabs and sidebar"
```

---

## Task 14: Extract `RequestEditorView` UserControl

The center panel's editor UI needs to be reusable per tab. Extract it into a UserControl whose DataContext is `RequestEditorViewModel`.

**Files:**
- Create: `src/PacketPanda.Desktop/Views/RequestEditorView.axaml`
- Create: `src/PacketPanda.Desktop/Views/RequestEditorView.axaml.cs`

- [ ] **Step 1: Write `RequestEditorView.axaml`**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:PacketPanda.Desktop.ViewModels"
             xmlns:conv="using:PacketPanda.Desktop.Converters"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="600"
             x:Class="PacketPanda.Desktop.Views.RequestEditorView"
             x:DataType="vm:RequestEditorViewModel">
    <Grid RowDefinitions="Auto,Auto,Auto,Auto,*" RowSpacing="8">
        <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="8">
            <Button Content="Open…" Command="{Binding OpenCommand}" />
            <Button Content="Save" Command="{Binding SaveCommand}" />
            <Button Content="Save As…" Command="{Binding SaveAsCommand}" />
        </StackPanel>

        <Grid Grid.Row="1" ColumnDefinitions="Auto,*,Auto" ColumnSpacing="8">
            <ComboBox Grid.Column="0"
                      ItemsSource="{Binding AvailableMethods}"
                      SelectedItem="{Binding SelectedMethod}"
                      MinWidth="100">
                <ComboBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Converter={x:Static conv:UpperCaseConverter.Instance}}" />
                    </DataTemplate>
                </ComboBox.ItemTemplate>
            </ComboBox>
            <TextBox Grid.Column="1"
                     Text="{Binding Url}"
                     Watermark="https://example.com/endpoint" />
            <Button Grid.Column="2"
                    Content="Send"
                    Command="{Binding SendCommand}" />
        </Grid>

        <TextBlock Grid.Row="2" Text="Body (JSON, optional)" Opacity="0.7" />

        <TextBox Grid.Row="3"
                 Text="{Binding RequestBody}"
                 AcceptsReturn="True"
                 TextWrapping="Wrap"
                 FontFamily="Consolas,Menlo,monospace"
                 Height="120"
                 Watermark="{}{ &quot;key&quot;: &quot;value&quot; }" />

        <Grid Grid.Row="4" RowDefinitions="Auto,Auto,Auto,*" RowSpacing="8">
            <TextBlock Grid.Row="0"
                       Text="{Binding ErrorMessage}"
                       Foreground="IndianRed"
                       TextWrapping="Wrap"
                       IsVisible="{Binding ErrorMessage, Converter={x:Static ObjectConverters.IsNotNull}}" />

            <StackPanel Grid.Row="1"
                        Orientation="Horizontal"
                        Spacing="12"
                        IsVisible="{Binding StatusCode, Converter={x:Static ObjectConverters.IsNotNull}}">
                <TextBlock Text="Status:" Opacity="0.7" />
                <TextBlock Text="{Binding StatusCode}" FontWeight="SemiBold" />
                <TextBlock Text="Elapsed:" Opacity="0.7" />
                <TextBlock Text="{Binding ElapsedMilliseconds, StringFormat='{}{0} ms'}" />
            </StackPanel>

            <TextBlock Grid.Row="2" Text="Response body" Opacity="0.7" />

            <TextBox Grid.Row="3"
                     Text="{Binding ResponseBody}"
                     IsReadOnly="True"
                     AcceptsReturn="True"
                     TextWrapping="Wrap"
                     FontFamily="Consolas,Menlo,monospace" />
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Write `RequestEditorView.axaml.cs`**

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PacketPanda.Desktop.Views;

public partial class RequestEditorView : UserControl
{
    public RequestEditorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Desktop/Views/RequestEditorView.axaml src/PacketPanda.Desktop/Views/RequestEditorView.axaml.cs
git commit -m "refactor(view): extract RequestEditorView UserControl"
```

---

## Task 15: Rewrite `MainWindow.axaml` with sidebar + tabs

**Files:**
- Modify: `src/PacketPanda.Desktop/Views/MainWindow.axaml`

- [ ] **Step 1: Replace the entire file contents**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:vm="using:PacketPanda.Desktop.ViewModels"
        xmlns:views="using:PacketPanda.Desktop.Views"
        mc:Ignorable="d" d:DesignWidth="1200" d:DesignHeight="800"
        x:Class="PacketPanda.Desktop.Views.MainWindow"
        x:DataType="vm:ShellViewModel"
        Title="PacketPanda"
        Width="1200" Height="800">
    <Grid ColumnDefinitions="280,4,*">
        <Grid Grid.Column="0" RowDefinitions="Auto,*" Background="#111">
            <Button Grid.Row="0"
                    Content="Open Folder…"
                    Command="{Binding Sidebar.OpenWorkspaceCommand}"
                    Margin="8"
                    HorizontalAlignment="Stretch" />

            <TreeView Grid.Row="1"
                      ItemsSource="{Binding Sidebar.RootNodes}">
                <TreeView.ItemTemplate>
                    <TreeDataTemplate ItemsSource="{Binding Children}"
                                      DataType="vm:CollectionNodeViewModel">
                        <TextBlock Text="{Binding Name}"
                                   DoubleTapped="OnNodeDoubleTapped" />
                    </TreeDataTemplate>
                </TreeView.ItemTemplate>
            </TreeView>
        </Grid>

        <GridSplitter Grid.Column="1" Background="#222" ResizeDirection="Columns" />

        <Grid Grid.Column="2" RowDefinitions="Auto,*">
            <Grid Grid.Row="0" ColumnDefinitions="*,Auto" Margin="4">
                <TabStrip ItemsSource="{Binding Tabs}"
                          SelectedItem="{Binding ActiveTab}">
                    <TabStrip.ItemTemplate>
                        <DataTemplate DataType="vm:TabViewModel">
                            <StackPanel Orientation="Horizontal" Spacing="6">
                                <TextBlock Text="{Binding Title}" VerticalAlignment="Center" />
                                <Button Content="×"
                                        Command="{Binding CloseCommand}"
                                        Padding="4,0"
                                        Background="Transparent"
                                        BorderThickness="0" />
                            </StackPanel>
                        </DataTemplate>
                    </TabStrip.ItemTemplate>
                </TabStrip>

                <Button Grid.Column="1"
                        Content="+"
                        Command="{Binding NewTabCommand}"
                        Margin="4,0,0,0"
                        Padding="8,2" />
            </Grid>

            <ContentControl Grid.Row="1"
                            Content="{Binding ActiveTab.Editor}"
                            Margin="12">
                <ContentControl.DataTemplates>
                    <DataTemplate DataType="vm:RequestEditorViewModel">
                        <views:RequestEditorView />
                    </DataTemplate>
                </ContentControl.DataTemplates>
            </ContentControl>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Add double-tap handler in `MainWindow.axaml.cs`**

Replace `src/PacketPanda.Desktop/Views/MainWindow.axaml.cs` with:

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: CollectionNodeViewModel node }) return;
        if (DataContext is not ShellViewModel shell) return;
        if (node.IsFolder)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        shell.Sidebar.OpenRequestCommand.Execute(node).Subscribe();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Run tests**

Run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(sidebar): wire MainWindow to ShellViewModel with TreeView and TabStrip"
```

---

## Task 16: Wire composition root in `App.axaml.cs` with auto-reopen

**Files:**
- Modify: `src/PacketPanda.Desktop/App.axaml.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using PacketPanda.Desktop.ViewModels;
using PacketPanda.Desktop.Views;

namespace PacketPanda.Desktop;

public partial class App : Application
{
    private HttpClient? _httpClient;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _httpClient = new HttpClient();
            var sender = new HttpRequestSender(_httpClient);
            var store = new TomlRequestFileStore();
            var scanner = new WorkspaceScanner();
            var settingsStore = new TomlSettingsStore(TomlSettingsStore.DefaultPath);

            var mainWindow = new MainWindow();

            var filePicker = new AvaloniaFilePickerService(mainWindow);
            var folderPicker = new AvaloniaFolderPickerService(mainWindow);
            var dialog = new AvaloniaDialogService(mainWindow);

            var sidebar = new CollectionSidebarViewModel(folderPicker, scanner, settingsStore);

            RequestEditorViewModel EditorFactory() => new(sender, store, filePicker);

            var shell = new ShellViewModel(sidebar, EditorFactory, dialog, store);
            mainWindow.DataContext = shell;

            desktop.MainWindow = mainWindow;

            _ = AutoReopenAsync(sidebar, settingsStore);

            desktop.Exit += (_, _) => _httpClient?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task AutoReopenAsync(CollectionSidebarViewModel sidebar, ISettingsStore settings)
    {
        try
        {
            var app = await settings.LoadAsync();
            if (!string.IsNullOrEmpty(app.LastWorkspacePath) && Directory.Exists(app.LastWorkspacePath))
                await sidebar.LoadWorkspaceAsync(app.LastWorkspacePath);
        }
        catch (SettingsException)
        {
            // Non-fatal — sidebar starts empty.
        }
    }
}
```

- [ ] **Step 2: Build + run tests**

Run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(sidebar): wire composition root with workspace auto-reopen"
```

---

## Task 17: Update documentation

**Files:**
- Modify: `CLAUDE.md`

The "Current state (vertical slice)" section in `CLAUDE.md` describes the left-panel placeholder and the lack of tabs — both now out of date.

- [ ] **Step 1: Update the "Current state" paragraph**

Open `CLAUDE.md` and replace the paragraph under "### Current state (vertical slice)" with:

```markdown
### Current state (vertical slice)

The request-editor + response-viewer flow is implemented, plus a collections sidebar and multi-tab editor. Users can open a folder of `.toml` requests, click a request to open it in a tab, edit and save through Open/Save/Save As, and have their workspace auto-reopen on the next launch. Environments, auth, scripting, and in-client tests remain future work per the parent design spec. Architectural seams in place: `PacketPanda.Core.Http.IHttpRequestSender`, `PacketPanda.Core.Persistence.IRequestFileStore`, `PacketPanda.Core.Persistence.IWorkspaceScanner`, `PacketPanda.Core.Persistence.ISettingsStore`, `PacketPanda.Desktop.Services.IFilePickerService`, `IFolderPickerService`, `IDialogService` — ViewModels depend on these and tests mock them with Moq.
```

And update the three-panel description to reflect the sidebar is live:

Replace:
```markdown
- **Left panel** — Collections sidebar: folders and saved requests loaded from disk
```
with (it's already accurate actually — leave it; just note the tab bar in the center panel by replacing):
```markdown
- **Center panel** — Tab bar + request editor: method, URL, headers, body, authentication. Multiple requests open in tabs with dirty indicators.
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(slice): document collections sidebar and tabs"
```

---

## Task 18: Manual smoke test

This task is not run by a subagent — it is a human-driven verification of the running app.

- [ ] **Step 1: Launch the app**

Run: `dotnet run --project src/PacketPanda.Desktop`

Expected: the app window opens with an empty collections sidebar on the left and a single untitled tab… actually, the shell starts with no tabs. Confirm the sidebar is visible with an "Open Folder…" button and the center panel is empty with a `+` button.

- [ ] **Step 2: Smoke — new tab + save**

1. Click `+` — a new "Untitled" tab opens with a blank request editor.
2. Type a URL `https://httpbin.org/get`. Tab title becomes `Untitled ●` (dirty).
3. Click `Save` — file picker appears; choose any folder, name it `smoke.toml`. Tab title becomes `smoke` (clean).

- [ ] **Step 3: Smoke — open folder**

1. Click `Open Folder…` in the sidebar, pick the folder containing `smoke.toml`.
2. The TreeView lists `smoke.toml`.
3. Double-click `smoke.toml` — opens (or focuses) a tab pre-populated with the URL you saved.

- [ ] **Step 4: Smoke — dirty-close confirmation**

1. Click `+` → type `https://example.com` in the URL field → tab is dirty.
2. Click the `×` on that tab — confirmation dialog appears. Click `Cancel`: tab stays. Click `×` again, click `Discard`: tab is removed.

- [ ] **Step 5: Smoke — auto-reopen**

1. Close the app.
2. Relaunch: `dotnet run --project src/PacketPanda.Desktop`.
3. Expected: the sidebar is pre-populated with the workspace you opened before (found via `%AppData%/PacketPanda/settings.toml`).

- [ ] **Step 6: Smoke — send request**

1. In any tab, set URL to `https://httpbin.org/get`, click `Send`.
2. Expected: right-panel (embedded under the editor) shows status 200 and response body within the tab.

No commit — manual verification only. Record outcomes in the PR description.

---

## Done criteria

- All tasks 1–17 committed on `task/collections-sidebar`.
- `dotnet test` passes (expect roughly 55+ tests across core + VM suites).
- Manual smoke test (Task 18) passes.
- `docs/superpowers/` remains uncommitted.
