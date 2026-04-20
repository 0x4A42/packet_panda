# TOML Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Open…, Save, and Save As… commands to the request editor that persist a single `HttpRequest` as a flat TOML file on disk.

**Architecture:** Two new seams in Core (`IRequestFileStore` + `RequestFileException`) handle TOML serialization and disk I/O. One new seam in Desktop (`IFilePickerService`) bridges the OS file picker without leaking Avalonia into the VM. The VM gains `CurrentFilePath`, `OpenCommand`, `SaveCommand`, and `SaveAsCommand`. The composition root wires everything together.

**Tech Stack:** C# / .NET 10, Avalonia 11, ReactiveUI, Tomlyn (TOML), xUnit + Moq + FluentAssertions

---

## File Map

**Create:**
- `src/PacketPanda.Core/Persistence/RequestFileException.cs` — custom exception wrapping all file/parse/validation failures
- `src/PacketPanda.Core/Persistence/IRequestFileStore.cs` — persistence seam: load/save `HttpRequest` ↔ disk
- `src/PacketPanda.Core/Persistence/TomlRequestFileStore.cs` — Tomlyn implementation of `IRequestFileStore`
- `src/PacketPanda.Desktop/Services/IFilePickerService.cs` — OS file picker seam
- `src/PacketPanda.Desktop/Services/AvaloniaFilePickerService.cs` — Avalonia `StorageProvider` implementation
- `tests/PacketPanda.Tests/Persistence/TomlRequestFileStoreTests.cs` — round-trip + error tests

**Modify:**
- `src/PacketPanda.Core/PacketPanda.Core.csproj` — add Tomlyn NuGet package
- `src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs` — add `CurrentFilePath`, `OpenCommand`, `SaveCommand`, `SaveAsCommand`
- `src/PacketPanda.Desktop/App.axaml.cs` — construct `TomlRequestFileStore` + `AvaloniaFilePickerService`, inject into VM
- `src/PacketPanda.Desktop/Views/MainWindow.axaml` — add toolbar row with three buttons

---

## Task 1: Add Tomlyn to Core

**Files:**
- Modify: `src/PacketPanda.Core/PacketPanda.Core.csproj`

- [ ] **Step 1: Add the Tomlyn package**

```bash
dotnet add src/PacketPanda.Core/PacketPanda.Core.csproj package Tomlyn
```

Expected: `PacketPanda.Core.csproj` now contains a `PackageReference` for Tomlyn.

- [ ] **Step 2: Build to confirm it resolves**

```bash
dotnet build
```

Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add src/PacketPanda.Core/PacketPanda.Core.csproj
git commit -m "chore(persistence): add Tomlyn package to Core"
```

---

## Task 2: Add `RequestFileException`

**Files:**
- Create: `src/PacketPanda.Core/Persistence/RequestFileException.cs`

- [ ] **Step 1: Create the exception class**

```csharp
namespace PacketPanda.Core.Persistence;

public sealed class RequestFileException : Exception
{
    public RequestFileException(string message) : base(message) { }
    public RequestFileException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/PacketPanda.Core/Persistence/RequestFileException.cs
git commit -m "feat(persistence): add RequestFileException"
```

---

## Task 3: Add `IRequestFileStore`

**Files:**
- Create: `src/PacketPanda.Core/Persistence/IRequestFileStore.cs`

- [ ] **Step 1: Write the failing test** (canary — will fail because interface doesn't exist yet, but we write the test first so its shape drives the interface)

Add to a new file `tests/PacketPanda.Tests/Persistence/TomlRequestFileStoreTests.cs`:

```csharp
using FluentAssertions;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Tests.Persistence;

public class TomlRequestFileStoreTests : IDisposable
{
    private readonly string _tempPath = Path.GetTempFileName() + ".toml";
    private readonly IRequestFileStore _store = new TomlRequestFileStore();

    public void Dispose()
    {
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [Fact]
    public async Task RoundTrip_preserves_method_url_and_body()
    {
        var request = new HttpRequest(HttpMethod.Post, "https://example.com", """{"a":1}""");

        await _store.SaveAsync(_tempPath, request);
        var loaded = await _store.LoadAsync(_tempPath);

        loaded.Method.Should().Be(HttpMethod.Post);
        loaded.Url.Should().Be("https://example.com");
        loaded.Body.Should().Be("""{"a":1}""");
    }
}
```

- [ ] **Step 2: Run to confirm it fails**

```bash
dotnet test --filter "TomlRequestFileStoreTests"
```

Expected: compile error — `IRequestFileStore` and `TomlRequestFileStore` not found.

- [ ] **Step 3: Create the interface**

```csharp
using PacketPanda.Core.Http;

namespace PacketPanda.Core.Persistence;

public interface IRequestFileStore
{
    Task<HttpRequest> LoadAsync(string path);
    Task SaveAsync(string path, HttpRequest request);
}
```

- [ ] **Step 4: Build**

```bash
dotnet build
```

Expected: Still fails because `TomlRequestFileStore` is missing — that's fine, next task adds it.

---

## Task 4: Implement `TomlRequestFileStore`

**Files:**
- Create: `src/PacketPanda.Core/Persistence/TomlRequestFileStore.cs`

- [ ] **Step 1: Create the implementation**

```csharp
using PacketPanda.Core.Http;
using Tomlyn;
using Tomlyn.Model;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Core.Persistence;

public sealed class TomlRequestFileStore : IRequestFileStore
{
    public async Task<HttpRequest> LoadAsync(string path)
    {
        string toml;
        try
        {
            toml = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            throw new RequestFileException($"Could not read file: {ex.Message}", ex);
        }

        TomlTable table;
        try
        {
            table = Toml.ToModel(toml);
        }
        catch (Exception ex)
        {
            throw new RequestFileException($"Invalid TOML: {ex.Message}", ex);
        }

        if (!table.TryGetValue("method", out var methodObj) || methodObj is not string methodStr)
            throw new RequestFileException("Missing required field 'method'.");

        if (!table.TryGetValue("url", out var urlObj) || urlObj is not string url)
            throw new RequestFileException("Missing required field 'url'.");

        if (!Enum.TryParse<HttpMethod>(methodStr, ignoreCase: true, out var method))
            throw new RequestFileException($"Unknown HTTP method '{methodStr}'. Expected GET, POST, PUT, or DELETE.");

        string? body = null;
        if (table.TryGetValue("body", out var bodyObj) && bodyObj is string bodyStr && bodyStr.Length > 0)
            body = bodyStr;

        return new HttpRequest(method, url, body);
    }

    public async Task SaveAsync(string path, HttpRequest request)
    {
        var table = new TomlTable
        {
            ["method"] = request.Method.ToString().ToUpperInvariant(),
            ["url"] = request.Url
        };

        if (!string.IsNullOrEmpty(request.Body))
            table["body"] = request.Body;

        var toml = Toml.FromModel(table);
        try
        {
            await File.WriteAllTextAsync(path, toml);
        }
        catch (Exception ex)
        {
            throw new RequestFileException($"Could not write file: {ex.Message}", ex);
        }
    }
}
```

- [ ] **Step 2: Run the round-trip test**

```bash
dotnet test --filter "RoundTrip_preserves_method_url_and_body"
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/PacketPanda.Core/Persistence/IRequestFileStore.cs
git add src/PacketPanda.Core/Persistence/TomlRequestFileStore.cs
git add tests/PacketPanda.Tests/Persistence/TomlRequestFileStoreTests.cs
git commit -m "feat(persistence): implement TomlRequestFileStore with round-trip test"
```

---

## Task 5: Remaining `TomlRequestFileStoreTests`

**Files:**
- Modify: `tests/PacketPanda.Tests/Persistence/TomlRequestFileStoreTests.cs`

Add each test below to the existing `TomlRequestFileStoreTests` class.

- [ ] **Step 1: Write all remaining tests**

```csharp
[Fact]
public async Task Null_body_is_omitted_from_file_and_loaded_as_null()
{
    var request = new HttpRequest(HttpMethod.Get, "https://example.com", null);

    await _store.SaveAsync(_tempPath, request);
    var content = await File.ReadAllTextAsync(_tempPath);
    var loaded = await _store.LoadAsync(_tempPath);

    content.Should().NotContain("body");
    loaded.Body.Should().BeNull();
}

[Fact]
public async Task Empty_body_is_treated_as_null()
{
    var request = new HttpRequest(HttpMethod.Get, "https://example.com", "");

    await _store.SaveAsync(_tempPath, request);
    var loaded = await _store.LoadAsync(_tempPath);

    loaded.Body.Should().BeNull();
}

[Fact]
public async Task Multiline_json_body_round_trips_correctly()
{
    var body = """
               {
                 "key": "value",
                 "nested": { "a": 1 }
               }
               """;
    var request = new HttpRequest(HttpMethod.Post, "https://example.com", body);

    await _store.SaveAsync(_tempPath, request);
    var loaded = await _store.LoadAsync(_tempPath);

    loaded.Body.Should().Be(body);
}

[Fact]
public async Task Load_missing_file_throws_RequestFileException()
{
    var act = async () => await _store.LoadAsync("/nonexistent/path/request.toml");

    await act.Should().ThrowAsync<RequestFileException>();
}

[Fact]
public async Task Load_invalid_toml_throws_RequestFileException()
{
    await File.WriteAllTextAsync(_tempPath, "this is not [ valid toml !!!");

    var act = async () => await _store.LoadAsync(_tempPath);

    await act.Should().ThrowAsync<RequestFileException>().WithMessage("Invalid TOML:*");
}

[Fact]
public async Task Load_missing_method_field_throws_RequestFileException()
{
    await File.WriteAllTextAsync(_tempPath, """url = "https://example.com" """);

    var act = async () => await _store.LoadAsync(_tempPath);

    await act.Should().ThrowAsync<RequestFileException>().WithMessage("*'method'*");
}

[Fact]
public async Task Load_missing_url_field_throws_RequestFileException()
{
    await File.WriteAllTextAsync(_tempPath, """method = "GET" """);

    var act = async () => await _store.LoadAsync(_tempPath);

    await act.Should().ThrowAsync<RequestFileException>().WithMessage("*'url'*");
}

[Fact]
public async Task Load_unknown_verb_throws_RequestFileException()
{
    await File.WriteAllTextAsync(_tempPath, """
        method = "PATCH"
        url = "https://example.com"
        """);

    var act = async () => await _store.LoadAsync(_tempPath);

    await act.Should().ThrowAsync<RequestFileException>().WithMessage("*PATCH*");
}

[Fact]
public async Task Load_lowercase_verb_succeeds()
{
    await File.WriteAllTextAsync(_tempPath, """
        method = "get"
        url = "https://example.com"
        """);

    var loaded = await _store.LoadAsync(_tempPath);

    loaded.Method.Should().Be(HttpMethod.Get);
}

[Fact]
public async Task Save_writes_method_as_uppercase()
{
    var request = new HttpRequest(HttpMethod.Get, "https://example.com");

    await _store.SaveAsync(_tempPath, request);
    var content = await File.ReadAllTextAsync(_tempPath);

    content.Should().Contain("GET");
    content.Should().NotContain("Get");
}
```

- [ ] **Step 2: Run all store tests**

```bash
dotnet test --filter "TomlRequestFileStoreTests"
```

Expected: All 11 tests pass.

- [ ] **Step 3: Run full test suite**

```bash
dotnet test
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/PacketPanda.Tests/Persistence/TomlRequestFileStoreTests.cs
git commit -m "test(persistence): complete TomlRequestFileStore test suite"
```

---

## Task 6: Add `IFilePickerService`

**Files:**
- Create: `src/PacketPanda.Desktop/Services/IFilePickerService.cs`
- Create: `src/PacketPanda.Desktop/Services/AvaloniaFilePickerService.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace PacketPanda.Desktop.Services;

public interface IFilePickerService
{
    Task<string?> PickOpenAsync();
    Task<string?> PickSaveAsync();
}
```

- [ ] **Step 2: Create the Avalonia implementation**

```csharp
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PacketPanda.Desktop.Services;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    private readonly Window _window;

    public AvaloniaFilePickerService(Window window)
    {
        _window = window;
    }

    public async Task<string?> PickOpenAsync()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Request",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("TOML request") { Patterns = ["*.toml"] }]
        });

        return files.Count == 1 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickSaveAsync()
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Request",
            DefaultExtension = "toml",
            FileTypeChoices = [new FilePickerFileType("TOML request") { Patterns = ["*.toml"] }]
        });

        return file?.Path.LocalPath;
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Desktop/Services/IFilePickerService.cs
git add src/PacketPanda.Desktop/Services/AvaloniaFilePickerService.cs
git commit -m "feat(persistence): add IFilePickerService and AvaloniaFilePickerService"
```

---

## Task 7: Add VM commands — tests first

**Files:**
- Modify: `tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs`

These tests will fail until Task 8 updates the VM.

- [ ] **Step 1: Add the new test cases**

Add the following `using` statements to the top of `MainWindowViewModelTests.cs` (alongside the existing ones):

```csharp
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
```

Add these tests to the existing `MainWindowViewModelTests` class. The class currently constructs the VM as `new MainWindowViewModel(_sender.Object)`. Update the `CreateVm()` helper to accept the two new mocks:

```csharp
// Add at the top of the class alongside _sender:
private readonly Mock<IRequestFileStore> _store = new(MockBehavior.Strict);
private readonly Mock<IFilePickerService> _picker = new(MockBehavior.Strict);

// Replace the existing CreateVm():
private MainWindowViewModel CreateVm() => new(_sender.Object, _store.Object, _picker.Object);
```

Then add these test methods:

```csharp
[Fact]
public async Task Open_populates_editor_fields_and_sets_CurrentFilePath_on_success()
{
    _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync("/tmp/req.toml");
    _store.Setup(s => s.LoadAsync("/tmp/req.toml"))
          .ReturnsAsync(new HttpRequest(HttpMethod.Post, "https://api.example.com", """{"a":1}"""));

    var vm = CreateVm();
    vm.StatusCode = 200;
    vm.ResponseBody = "stale";
    vm.ElapsedMilliseconds = 50;
    vm.ErrorMessage = "old error";

    await vm.OpenCommand.Execute();

    vm.SelectedMethod.Should().Be(HttpMethod.Post);
    vm.Url.Should().Be("https://api.example.com");
    vm.RequestBody.Should().Be("""{"a":1}""");
    vm.StatusCode.Should().BeNull();
    vm.ResponseBody.Should().BeNull();
    vm.ElapsedMilliseconds.Should().BeNull();
    vm.ErrorMessage.Should().BeNull();
    vm.CurrentFilePath.Should().Be("/tmp/req.toml");
}

[Fact]
public async Task Open_cancelled_does_nothing()
{
    _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync((string?)null);
    var vm = CreateVm();
    vm.Url = "https://original.com";

    await vm.OpenCommand.Execute();

    vm.Url.Should().Be("https://original.com");
    vm.CurrentFilePath.Should().BeNull();
    _store.Verify(s => s.LoadAsync(It.IsAny<string>()), Times.Never);
}

[Fact]
public async Task Open_failure_sets_ErrorMessage_and_leaves_state_unchanged()
{
    _picker.Setup(p => p.PickOpenAsync()).ReturnsAsync("/tmp/bad.toml");
    _store.Setup(s => s.LoadAsync("/tmp/bad.toml"))
          .ThrowsAsync(new RequestFileException("Invalid TOML: unexpected token"));

    var vm = CreateVm();
    vm.Url = "https://original.com";

    await vm.OpenCommand.Execute();

    vm.ErrorMessage.Should().Be("Invalid TOML: unexpected token");
    vm.Url.Should().Be("https://original.com");
    vm.CurrentFilePath.Should().BeNull();
}

[Fact]
public async Task Save_writes_to_CurrentFilePath_without_picker_when_path_is_set()
{
    var vm = CreateVm();
    vm.Url = "https://example.com";
    vm.SelectedMethod = HttpMethod.Delete;
    vm.CurrentFilePath = "/tmp/existing.toml";

    _store.Setup(s => s.SaveAsync("/tmp/existing.toml",
        It.Is<HttpRequest>(r => r.Url == "https://example.com" && r.Method == HttpMethod.Delete)))
        .Returns(Task.CompletedTask);

    await vm.SaveCommand.Execute();

    _store.Verify(s => s.SaveAsync("/tmp/existing.toml", It.IsAny<HttpRequest>()), Times.Once);
    _picker.Verify(p => p.PickSaveAsync(), Times.Never);
}

[Fact]
public async Task Save_invokes_picker_and_saves_when_CurrentFilePath_is_null()
{
    _picker.Setup(p => p.PickSaveAsync()).ReturnsAsync("/tmp/new.toml");
    _store.Setup(s => s.SaveAsync("/tmp/new.toml", It.IsAny<HttpRequest>()))
          .Returns(Task.CompletedTask);

    var vm = CreateVm();
    vm.Url = "https://example.com";

    await vm.SaveCommand.Execute();

    vm.CurrentFilePath.Should().Be("/tmp/new.toml");
    _store.Verify(s => s.SaveAsync("/tmp/new.toml", It.IsAny<HttpRequest>()), Times.Once);
}

[Fact]
public async Task SaveCommand_CanExecute_is_false_when_url_is_empty()
{
    var vm = CreateVm();
    vm.Url = "";

    var canExecute = await vm.SaveCommand.CanExecute.FirstAsync();

    canExecute.Should().BeFalse();
}

[Fact]
public async Task SaveAs_always_invokes_picker_and_updates_CurrentFilePath()
{
    _picker.Setup(p => p.PickSaveAsync()).ReturnsAsync("/tmp/saveas.toml");
    _store.Setup(s => s.SaveAsync("/tmp/saveas.toml", It.IsAny<HttpRequest>()))
          .Returns(Task.CompletedTask);

    var vm = CreateVm();
    vm.Url = "https://example.com";
    vm.CurrentFilePath = "/tmp/old.toml";

    await vm.SaveAsCommand.Execute();

    vm.CurrentFilePath.Should().Be("/tmp/saveas.toml");
    _picker.Verify(p => p.PickSaveAsync(), Times.Once);
}

[Fact]
public async Task SaveAs_cancelled_leaves_CurrentFilePath_unchanged()
{
    _picker.Setup(p => p.PickSaveAsync()).ReturnsAsync((string?)null);

    var vm = CreateVm();
    vm.Url = "https://example.com";
    vm.CurrentFilePath = "/tmp/original.toml";

    await vm.SaveAsCommand.Execute();

    vm.CurrentFilePath.Should().Be("/tmp/original.toml");
    _store.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<HttpRequest>()), Times.Never);
}

[Fact]
public async Task Save_failure_sets_ErrorMessage_and_leaves_CurrentFilePath_unchanged()
{
    _store.Setup(s => s.SaveAsync("/tmp/req.toml", It.IsAny<HttpRequest>()))
          .ThrowsAsync(new RequestFileException("Could not write file: access denied"));

    var vm = CreateVm();
    vm.Url = "https://example.com";
    vm.CurrentFilePath = "/tmp/req.toml";

    await vm.SaveCommand.Execute();

    vm.ErrorMessage.Should().Be("Could not write file: access denied");
    vm.CurrentFilePath.Should().Be("/tmp/req.toml");
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test --filter "MainWindowViewModelTests"
```

Expected: Compile error — `MainWindowViewModel` constructor doesn't accept `IRequestFileStore` and `IFilePickerService` yet.

---

## Task 8: Update `MainWindowViewModel`

**Files:**
- Modify: `src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Replace the full file contents**

```csharp
using System.Reactive;
using System.Reactive.Linq;
using PacketPanda.Core.Http;
using PacketPanda.Core.Persistence;
using PacketPanda.Desktop.Services;
using ReactiveUI;
using HttpMethod = PacketPanda.Core.Http.HttpMethod;

namespace PacketPanda.Desktop.ViewModels;

public class MainWindowViewModel : ViewModelBase
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

    public MainWindowViewModel(IHttpRequestSender sender, IRequestFileStore store, IFilePickerService picker)
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

- [ ] **Step 2: Run VM tests**

```bash
dotnet test --filter "MainWindowViewModelTests"
```

Expected: All tests pass (both existing send tests and all new persistence tests).

- [ ] **Step 3: Run full test suite**

```bash
dotnet test
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs
git add tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat(persistence): add Open/Save/SaveAs commands to MainWindowViewModel"
```

---

## Task 9: Wire up composition root

**Files:**
- Modify: `src/PacketPanda.Desktop/App.axaml.cs`

- [ ] **Step 1: Update `App.axaml.cs`**

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

            var window = new MainWindow();
            var picker = new AvaloniaFilePickerService(window);

            window.DataContext = new MainWindowViewModel(sender, store, picker);
            desktop.MainWindow = window;

            desktop.Exit += (_, _) => _httpClient?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 3: Run full test suite**

```bash
dotnet test
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Desktop/App.axaml.cs
git commit -m "feat(persistence): wire TomlRequestFileStore and AvaloniaFilePickerService in composition root"
```

---

## Task 10: Add toolbar to `MainWindow.axaml`

**Files:**
- Modify: `src/PacketPanda.Desktop/Views/MainWindow.axaml`

- [ ] **Step 1: Add the toolbar row to the center panel**

The center panel (`Grid.Column="2"`) currently has `RowDefinitions="Auto,Auto,*"`. Change it to `RowDefinitions="Auto,Auto,Auto,*"` and insert a toolbar row as the first row (pushing existing rows down by one).

Replace the entire center panel `<Border Grid.Column="2" ...>` block with:

```xml
<Border Grid.Column="2" Padding="12">
    <Grid RowDefinitions="Auto,Auto,Auto,*" RowSpacing="8">
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
                 Watermark="{}{ &quot;key&quot;: &quot;value&quot; }" />
    </Grid>
</Border>
```

- [ ] **Step 2: Build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 3: Run full test suite**

```bash
dotnet test
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Desktop/Views/MainWindow.axaml
git commit -m "feat(persistence): add Open/Save/SaveAs toolbar to MainWindow"
```

---

## Task 11: Smoke test the UI

- [ ] **Step 1: Run the app**

```bash
dotnet run --project src/PacketPanda.Desktop
```

- [ ] **Step 2: Exercise the golden path**

1. Enter a URL and select a method in the editor.
2. Click **Save As…** — OS file picker appears. Save to a `.toml` file.
3. Clear the URL field. Click **Open…** — pick the same file. Editor fields repopulate.
4. Click **Save** — no picker appears; it saves directly to the existing path.
5. Change the URL. Click **Save As…** again to a new path — `CurrentFilePath` updates (observe that subsequent Save goes to the new path).

- [ ] **Step 3: Exercise error paths**

1. Manually edit the saved `.toml` to remove `url =`. Click **Open…** and pick it. `ErrorMessage` appears in the response panel; editor state unchanged.
2. Click **Open…** and cancel without picking a file — nothing changes.
3. Click **Save As…** and cancel — `CurrentFilePath` unchanged.

- [ ] **Step 4: Final test run**

```bash
dotnet test
```

Expected: All tests pass.

- [ ] **Step 5: Final commit if any fixups were needed**

If step 2–3 revealed any issues, fix them, re-run `dotnet test`, and commit the fixups before moving on.
