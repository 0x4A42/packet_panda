# Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a minimum-usable Avalonia window that sends `GET`/`POST`/`PUT`/`DELETE` requests to a user-entered URL (with optional JSON body) and displays status, timing, and response body, proving the full Avalonia + ReactiveUI + HttpClient loop end-to-end.

**Architecture:** `PacketPanda.Core` owns the HTTP domain model and a single `IHttpRequestSender` seam. `PacketPanda.Desktop` owns the window, the `MainWindowViewModel` (which depends on the seam), and the composition root in `App.axaml.cs`. No DI container; manual wiring. Tests cover the ViewModel with Moq and the real sender with `WireMock.Net`.

**Tech Stack:** C# / .NET 10, Avalonia 11.3.8, Avalonia.ReactiveUI, xUnit, Moq, FluentAssertions 7.2.0 (pinned), WireMock.Net (new dependency).

**Spec:** [`docs/superpowers/specs/2026-04-18-vertical-slice-design.md`](../specs/2026-04-18-vertical-slice-design.md)

**Workflow reminders from `CLAUDE.md`:**
- All work happens on branch `task/vertical-slice`.
- Run `dotnet test` before every commit; only commit if green.
- Commit messages use semantic format: `feat(slice): …`, `test(slice): …`, `docs(slice): …`, `chore(slice): …`.
- Use `FluentAssertions` (never `Assert.X`). Pinned to **7.2.0** — do not upgrade.
- Use `Moq` for mocks. No other mocking libraries.
- Keep docs (`CLAUDE.md`, specs, README) in sync with code in the same unit of work.

**Implementation notes:**
- For reactive properties on the ViewModel, use the built-in `this.RaiseAndSetIfChanged(ref _field, value)` pattern. Do not add `ReactiveUI.Fody` or other source-generator packages in this slice — keep dependencies minimal.
- The existing scaffold has `ViewModelBase : ReactiveObject`, `MainWindowViewModel : ViewModelBase` (empty), and a 3-panel `MainWindow.axaml`. This plan replaces the center and right panels with slice UI and leaves the left "Collections" placeholder alone.
- The test project (`tests/PacketPanda.Tests`) already references `PacketPanda.Core`. ViewModel tests need a reference to `PacketPanda.Desktop` — add it in Task 4.

---

## File Structure

### `PacketPanda.Core` (create)
- `src/PacketPanda.Core/Http/HttpMethod.cs` — the method enum. Namespace `PacketPanda.Core.Http` to avoid clashing with `System.Net.Http.HttpMethod`.
- `src/PacketPanda.Core/Http/HttpRequest.cs` — request record.
- `src/PacketPanda.Core/Http/HttpResponse.cs` — response record.
- `src/PacketPanda.Core/Http/SendResult.cs` — discriminated result (sealed base + two subtypes).
- `src/PacketPanda.Core/Http/IHttpRequestSender.cs` — the seam.
- `src/PacketPanda.Core/Http/HttpRequestSender.cs` — real implementation.

### `PacketPanda.Desktop` (modify)
- `src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs` — gains reactive properties and `SendCommand`.
- `src/PacketPanda.Desktop/Views/MainWindow.axaml` — request editor + response viewer panels.
- `src/PacketPanda.Desktop/App.axaml.cs` — composition root constructs `HttpClient` + `HttpRequestSender` + VM.

### `PacketPanda.Tests` (create/modify)
- `tests/PacketPanda.Tests/Http/HttpRequestSenderTests.cs` — integration tests via WireMock.Net.
- `tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs` — unit tests with mocked sender.
- `tests/PacketPanda.Tests/PacketPanda.Tests.csproj` — add `WireMock.Net` package + reference to `PacketPanda.Desktop`.

### Docs (modify)
- `CLAUDE.md` — reflect actual v1 slice state (what's implemented vs. deferred).
- `README.md` — one line on what runs today.

---

## Task 1: Create branch and set up Core/Http folder

**Files:**
- Modify: working branch (git)

- [ ] **Step 1: Create and check out the feature branch**

Run:
```bash
git checkout main
git pull
git checkout -b task/vertical-slice
```

- [ ] **Step 2: Verify the baseline is green before touching anything**

Run:
```bash
dotnet test
```

Expected: all existing tests pass (at minimum `CoreSmokeTests.Version_IsNotEmpty`). If anything fails, stop and investigate — don't start stacking changes on a red build.

---

## Task 2: Add `HttpMethod` enum

**Files:**
- Create: `src/PacketPanda.Core/Http/HttpMethod.cs`
- Test: `tests/PacketPanda.Tests/Http/HttpMethodTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/PacketPanda.Tests/Http/HttpMethodTests.cs`:

```csharp
using FluentAssertions;
using PacketPanda.Core.Http;

namespace PacketPanda.Tests.Http;

public class HttpMethodTests
{
    [Fact]
    public void Enum_has_exactly_the_four_v1_methods()
    {
        Enum.GetNames<HttpMethod>()
            .Should()
            .BeEquivalentTo("Get", "Post", "Put", "Delete");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~HttpMethodTests`
Expected: FAIL — `HttpMethod` / `PacketPanda.Core.Http` namespace does not exist.

- [ ] **Step 3: Create the enum**

Create `src/PacketPanda.Core/Http/HttpMethod.cs`:

```csharp
namespace PacketPanda.Core.Http;

public enum HttpMethod
{
    Get,
    Post,
    Put,
    Delete
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~HttpMethodTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PacketPanda.Core/Http/HttpMethod.cs tests/PacketPanda.Tests/Http/HttpMethodTests.cs
git commit -m "feat(slice): add HttpMethod enum for v1 verbs"
```

---

## Task 3: Add `HttpRequest` and `HttpResponse` records

**Files:**
- Create: `src/PacketPanda.Core/Http/HttpRequest.cs`
- Create: `src/PacketPanda.Core/Http/HttpResponse.cs`

These are plain data carriers. Records give value-equality for free — that's all the testing they need at this layer; they're exercised thoroughly by downstream tests.

- [ ] **Step 1: Create `HttpRequest`**

Create `src/PacketPanda.Core/Http/HttpRequest.cs`:

```csharp
namespace PacketPanda.Core.Http;

public sealed record HttpRequest(HttpMethod Method, string Url, string? Body = null);
```

- [ ] **Step 2: Create `HttpResponse`**

Create `src/PacketPanda.Core/Http/HttpResponse.cs`:

```csharp
namespace PacketPanda.Core.Http;

public sealed record HttpResponse(int StatusCode, string Body, long ElapsedMilliseconds);
```

- [ ] **Step 3: Build to confirm nothing is broken**

Run: `dotnet build`
Expected: build succeeds with zero warnings (project has `TreatWarningsAsErrors=true`).

- [ ] **Step 4: Commit**

```bash
git add src/PacketPanda.Core/Http/HttpRequest.cs src/PacketPanda.Core/Http/HttpResponse.cs
git commit -m "feat(slice): add HttpRequest and HttpResponse records"
```

---

## Task 4: Add `SendResult` discriminated result

**Files:**
- Create: `src/PacketPanda.Core/Http/SendResult.cs`
- Test: `tests/PacketPanda.Tests/Http/SendResultTests.cs`

The `SendResult` is a sealed base with two subtypes — `SendResult.Ok` (wraps `HttpResponse`) and `SendResult.Error` (wraps a message). Pattern-matchable.

- [ ] **Step 1: Write the failing test**

Create `tests/PacketPanda.Tests/Http/SendResultTests.cs`:

```csharp
using FluentAssertions;
using PacketPanda.Core.Http;

namespace PacketPanda.Tests.Http;

public class SendResultTests
{
    [Fact]
    public void Ok_wraps_a_response()
    {
        var response = new HttpResponse(200, "hello", 42);
        var result = new SendResult.Ok(response);

        result.Response.Should().BeSameAs(response);
    }

    [Fact]
    public void Error_wraps_a_message()
    {
        var result = new SendResult.Error("connection refused");

        result.Message.Should().Be("connection refused");
    }

    [Fact]
    public void Pattern_match_distinguishes_the_cases()
    {
        SendResult ok = new SendResult.Ok(new HttpResponse(200, "body", 1));
        SendResult err = new SendResult.Error("boom");

        Describe(ok).Should().Be("ok:200");
        Describe(err).Should().Be("err:boom");

        static string Describe(SendResult r) => r switch
        {
            SendResult.Ok o => $"ok:{o.Response.StatusCode}",
            SendResult.Error e => $"err:{e.Message}",
            _ => throw new InvalidOperationException()
        };
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~SendResultTests`
Expected: FAIL — `SendResult` does not exist.

- [ ] **Step 3: Create `SendResult`**

Create `src/PacketPanda.Core/Http/SendResult.cs`:

```csharp
namespace PacketPanda.Core.Http;

public abstract record SendResult
{
    private SendResult() { }

    public sealed record Ok(HttpResponse Response) : SendResult;

    public sealed record Error(string Message) : SendResult;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~SendResultTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/PacketPanda.Core/Http/SendResult.cs tests/PacketPanda.Tests/Http/SendResultTests.cs
git commit -m "feat(slice): add SendResult discriminated result type"
```

---

## Task 5: Add `IHttpRequestSender` interface

**Files:**
- Create: `src/PacketPanda.Core/Http/IHttpRequestSender.cs`

No test for an empty interface — it's exercised by the implementation tests in Task 6 and the VM tests in Task 8.

- [ ] **Step 1: Create the interface**

Create `src/PacketPanda.Core/Http/IHttpRequestSender.cs`:

```csharp
namespace PacketPanda.Core.Http;

public interface IHttpRequestSender
{
    Task<SendResult> SendAsync(HttpRequest request, CancellationToken cancellationToken = default);
}
```

Note the `CancellationToken` parameter is included now even though cancellation UX is deferred — it costs nothing to accept it, and leaving it off means every call site breaks when cancellation lands.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success, no warnings.

- [ ] **Step 3: Commit**

```bash
git add src/PacketPanda.Core/Http/IHttpRequestSender.cs
git commit -m "feat(slice): add IHttpRequestSender seam"
```

---

## Task 6: Implement `HttpRequestSender` with integration tests

**Files:**
- Create: `src/PacketPanda.Core/Http/HttpRequestSender.cs`
- Create: `tests/PacketPanda.Tests/Http/HttpRequestSenderTests.cs`
- Modify: `tests/PacketPanda.Tests/PacketPanda.Tests.csproj` (add WireMock.Net)

- [ ] **Step 1: Add the WireMock.Net package to the test project**

Run:
```bash
dotnet add tests/PacketPanda.Tests/PacketPanda.Tests.csproj package WireMock.Net
```

Expected: the `<PackageReference Include="WireMock.Net" ... />` line is added to the csproj. Verify by opening the file.

- [ ] **Step 2: Write the failing integration tests**

Create `tests/PacketPanda.Tests/Http/HttpRequestSenderTests.cs`:

```csharp
using System.Net.Http;
using FluentAssertions;
using PacketPanda.Core.Http;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace PacketPanda.Tests.Http;

public class HttpRequestSenderTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly HttpClient _httpClient;
    private readonly HttpRequestSender _sender;

    public HttpRequestSenderTests()
    {
        _server = WireMockServer.Start();
        _httpClient = new HttpClient();
        _sender = new HttpRequestSender(_httpClient);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        _httpClient.Dispose();
    }

    [Fact]
    public async Task Get_returning_200_yields_Ok_with_status_body_and_timing()
    {
        _server
            .Given(Request.Create().WithPath("/hello").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("hi"));

        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, _server.Url + "/hello"));

        result.Should().BeOfType<SendResult.Ok>();
        var ok = (SendResult.Ok)result;
        ok.Response.StatusCode.Should().Be(200);
        ok.Response.Body.Should().Be("hi");
        ok.Response.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Non_2xx_response_is_returned_as_Ok_not_Error()
    {
        _server
            .Given(Request.Create().WithPath("/missing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404).WithBody("nope"));

        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, _server.Url + "/missing"));

        result.Should().BeOfType<SendResult.Ok>();
        var ok = (SendResult.Ok)result;
        ok.Response.StatusCode.Should().Be(404);
        ok.Response.Body.Should().Be("nope");
    }

    [Fact]
    public async Task Unreachable_host_yields_Error()
    {
        // Port 1 is almost always closed; connection attempt will fail fast.
        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, "http://127.0.0.1:1/"));

        result.Should().BeOfType<SendResult.Error>();
        ((SendResult.Error)result).Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Post_with_body_sends_json_content_type_and_body()
    {
        _server
            .Given(Request.Create().WithPath("/echo").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("created"));

        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Post, _server.Url + "/echo", "{\"a\":1}"));

        result.Should().BeOfType<SendResult.Ok>();
        var received = _server.LogEntries.Single();
        received.RequestMessage.Body.Should().Be("{\"a\":1}");
        received.RequestMessage.Headers!["Content-Type"].Single()
            .Should().StartWith("application/json");
    }

    [Fact]
    public async Task Malformed_url_yields_Error_not_exception()
    {
        var result = await _sender.SendAsync(
            new HttpRequest(HttpMethod.Get, "not a url"));

        result.Should().BeOfType<SendResult.Error>();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~HttpRequestSenderTests`
Expected: FAIL — `HttpRequestSender` does not exist.

- [ ] **Step 4: Implement `HttpRequestSender`**

Create `src/PacketPanda.Core/Http/HttpRequestSender.cs`:

```csharp
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using SystemHttpMethod = System.Net.Http.HttpMethod;

namespace PacketPanda.Core.Http;

public sealed class HttpRequestSender : IHttpRequestSender
{
    private readonly HttpClient _httpClient;

    public HttpRequestSender(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SendResult> SendAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage message;
        try
        {
            message = BuildMessage(request);
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException or ArgumentException)
        {
            return new SendResult.Error(ex.Message);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new SendResult.Ok(new HttpResponse((int)response.StatusCode, body, stopwatch.ElapsedMilliseconds));
        }
        catch (HttpRequestException ex)
        {
            return new SendResult.Error(ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout (HttpClient.Timeout). True user cancellation rethrows.
            return new SendResult.Error(ex.Message);
        }
        finally
        {
            message.Dispose();
        }
    }

    private static HttpRequestMessage BuildMessage(HttpRequest request)
    {
        var method = request.Method switch
        {
            HttpMethod.Get => SystemHttpMethod.Get,
            HttpMethod.Post => SystemHttpMethod.Post,
            HttpMethod.Put => SystemHttpMethod.Put,
            HttpMethod.Delete => SystemHttpMethod.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Method, "Unsupported method")
        };

        var message = new HttpRequestMessage(method, request.Url);
        if (!string.IsNullOrEmpty(request.Body))
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }
        return message;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~HttpRequestSenderTests`
Expected: PASS (5 tests).

If the unreachable-host test is flaky in your environment (some networks answer on port 1 with a reset that maps differently), swap `127.0.0.1:1` for a guaranteed-closed port such as one the test itself binds and then closes. Don't paper over a real failure by relaxing the assertion.

- [ ] **Step 6: Commit**

```bash
git add src/PacketPanda.Core/Http/HttpRequestSender.cs tests/PacketPanda.Tests/Http/HttpRequestSenderTests.cs tests/PacketPanda.Tests/PacketPanda.Tests.csproj
git commit -m "feat(slice): implement HttpRequestSender against WireMock.Net"
```

---

## Task 7: Add Desktop project reference to the test project

**Files:**
- Modify: `tests/PacketPanda.Tests/PacketPanda.Tests.csproj`

Needed so the VM tests in Task 8 can see `MainWindowViewModel`.

- [ ] **Step 1: Add the reference**

Run:
```bash
dotnet add tests/PacketPanda.Tests/PacketPanda.Tests.csproj reference src/PacketPanda.Desktop/PacketPanda.Desktop.csproj
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add tests/PacketPanda.Tests/PacketPanda.Tests.csproj
git commit -m "chore(slice): reference Desktop from test project for VM tests"
```

---

## Task 8: Implement `MainWindowViewModel` with unit tests

**Files:**
- Modify: `src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs`
- Create: `tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs`

The VM exposes reactive properties (via `RaiseAndSetIfChanged`) and a `ReactiveCommand` whose `canExecute` is `Url` non-empty AND not already running.

- [ ] **Step 1: Write the failing tests**

Create `tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs`:

```csharp
using System.Reactive.Linq;
using FluentAssertions;
using Moq;
using PacketPanda.Core.Http;
using PacketPanda.Desktop.ViewModels;

namespace PacketPanda.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private readonly Mock<IHttpRequestSender> _sender = new(MockBehavior.Strict);

    private MainWindowViewModel CreateVm() => new(_sender.Object);

    [Fact]
    public async Task Send_populates_response_fields_from_Ok_result()
    {
        _sender
            .Setup(s => s.SendAsync(
                It.Is<HttpRequest>(r => r.Method == HttpMethod.Get && r.Url == "https://example.com"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "hi", 42)));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.SelectedMethod = HttpMethod.Get;

        await vm.SendCommand.Execute();

        vm.StatusCode.Should().Be(200);
        vm.ResponseBody.Should().Be("hi");
        vm.ElapsedMilliseconds.Should().Be(42);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Send_populates_error_and_clears_response_fields_on_Error_result()
    {
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendResult.Error("connection refused"));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.StatusCode = 999;
        vm.ResponseBody = "stale";
        vm.ElapsedMilliseconds = 123;

        await vm.SendCommand.Execute();

        vm.ErrorMessage.Should().Be("connection refused");
        vm.StatusCode.Should().BeNull();
        vm.ResponseBody.Should().BeNull();
        vm.ElapsedMilliseconds.Should().BeNull();
    }

    [Fact]
    public async Task CanExecute_is_false_when_url_is_empty()
    {
        var vm = CreateVm();
        vm.Url = "";

        var canExecute = await vm.SendCommand.CanExecute.FirstAsync();

        canExecute.Should().BeFalse();
    }

    [Fact]
    public async Task CanExecute_is_true_when_url_is_non_empty()
    {
        var vm = CreateVm();
        vm.Url = "https://example.com";

        var canExecute = await vm.SendCommand.CanExecute.FirstAsync();

        canExecute.Should().BeTrue();
    }

    [Fact]
    public async Task Send_forwards_non_empty_body_to_sender()
    {
        HttpRequest? captured = null;
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "", 0)));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.SelectedMethod = HttpMethod.Post;
        vm.RequestBody = "{\"x\":1}";

        await vm.SendCommand.Execute();

        captured.Should().NotBeNull();
        captured!.Body.Should().Be("{\"x\":1}");
    }

    [Fact]
    public async Task Send_forwards_null_body_when_request_body_is_empty()
    {
        HttpRequest? captured = null;
        _sender
            .Setup(s => s.SendAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .Callback<HttpRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new SendResult.Ok(new HttpResponse(200, "", 0)));

        var vm = CreateVm();
        vm.Url = "https://example.com";
        vm.RequestBody = "";

        await vm.SendCommand.Execute();

        captured!.Body.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~MainWindowViewModelTests`
Expected: FAIL — VM has no constructor taking `IHttpRequestSender`, no properties, no `SendCommand`.

- [ ] **Step 3: Implement the ViewModel**

Replace the contents of `src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs`:

```csharp
using System.Reactive;
using System.Reactive.Linq;
using PacketPanda.Core.Http;
using ReactiveUI;

namespace PacketPanda.Desktop.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IHttpRequestSender _sender;

    private HttpMethod _selectedMethod = HttpMethod.Get;
    private string _url = string.Empty;
    private string? _requestBody;
    private int? _statusCode;
    private long? _elapsedMilliseconds;
    private string? _responseBody;
    private string? _errorMessage;

    public MainWindowViewModel(IHttpRequestSender sender)
    {
        _sender = sender;

        var canSend = this.WhenAnyValue(vm => vm.Url, url => !string.IsNullOrWhiteSpace(url));
        SendCommand = ReactiveCommand.CreateFromTask(SendAsync, canSend);
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

    public ReactiveCommand<Unit, Unit> SendCommand { get; }

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
}
```

Note: the existing `App.axaml.cs` currently does `new MainWindowViewModel()` (no args). That file is updated in Task 10 — the build will be red between now and then, which is why Task 9 (the view) and Task 10 (the composition root) come before the next test run. That's deliberate: the VM change, the view change, and the composition root change together form one landable unit.

- [ ] **Step 4: Build to confirm the VM compiles in isolation**

Run: `dotnet build src/PacketPanda.Core src/PacketPanda.Desktop/PacketPanda.Desktop.csproj` is NOT what we want — just run the full build and accept the `App.axaml.cs` failure temporarily. Alternative: skip straight to Task 9 without running the build. Either is fine; don't commit in between.

Do NOT commit yet — the VM change, UI change, and composition root change commit together after Task 10.

---

## Task 9: Build the request editor and response viewer UI

**Files:**
- Modify: `src/PacketPanda.Desktop/Views/MainWindow.axaml`

Replace the center panel's "Request" placeholder with the editor and the right panel's "Response" placeholder with the viewer. Leave the left "Collections" panel as-is.

- [ ] **Step 1: Update the XAML**

Replace the contents of `src/PacketPanda.Desktop/Views/MainWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:vm="using:PacketPanda.Desktop.ViewModels"
        xmlns:http="using:PacketPanda.Core.Http"
        mc:Ignorable="d" d:DesignWidth="1200" d:DesignHeight="800"
        x:Class="PacketPanda.Desktop.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Title="PacketPanda"
        Width="1200" Height="800">
    <Grid ColumnDefinitions="250,4,*,4,400">
        <Border Grid.Column="0" Padding="12">
            <TextBlock Text="Collections" FontWeight="SemiBold" />
        </Border>

        <GridSplitter Grid.Column="1" Background="#222" ResizeDirection="Columns" />

        <Border Grid.Column="2" Padding="12">
            <Grid RowDefinitions="Auto,Auto,*" RowSpacing="8">
                <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" ColumnSpacing="8">
                    <ComboBox Grid.Column="0"
                              ItemsSource="{Binding AvailableMethods}"
                              SelectedItem="{Binding SelectedMethod}"
                              MinWidth="100" />
                    <TextBox Grid.Column="1"
                             Text="{Binding Url}"
                             Watermark="https://example.com/endpoint" />
                    <Button Grid.Column="2"
                            Content="Send"
                            Command="{Binding SendCommand}" />
                </Grid>

                <TextBlock Grid.Row="1" Text="Body (JSON, optional)" Opacity="0.7" />

                <TextBox Grid.Row="2"
                         Text="{Binding RequestBody}"
                         AcceptsReturn="True"
                         TextWrapping="Wrap"
                         FontFamily="Consolas,Menlo,monospace"
                         Watermark="{}{ &quot;key&quot;: &quot;value&quot; }" />
            </Grid>
        </Border>

        <GridSplitter Grid.Column="3" Background="#222" ResizeDirection="Columns" />

        <Border Grid.Column="4" Padding="12">
            <Grid RowDefinitions="Auto,Auto,Auto,*" RowSpacing="8">
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
        </Border>
    </Grid>
</Window>
```

Do not commit yet — still tied to Task 10.

---

## Task 10: Wire the composition root and commit the UI slice

**Files:**
- Modify: `src/PacketPanda.Desktop/App.axaml.cs`

- [ ] **Step 1: Update `App.axaml.cs`**

Replace the contents of `src/PacketPanda.Desktop/App.axaml.cs`:

```csharp
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PacketPanda.Core.Http;
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

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(sender)
            };

            desktop.Exit += (_, _) => _httpClient?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: all tests pass (smoke, HttpMethod, SendResult, HttpRequestSender, MainWindowViewModel).

- [ ] **Step 3: Manual smoke test**

Run:
```bash
dotnet run --project src/PacketPanda.Desktop
```

Verify in the running window:
1. Type `https://httpbin.org/get` into the URL box, click Send → status 200, elapsed shown, JSON body appears.
2. Switch method to `POST`, URL `https://httpbin.org/post`, body `{"hello":"world"}`, click Send → status 200, body echoes the sent payload under `"json"`.
3. Clear the URL box → Send button is disabled.
4. Type a bogus URL `http://127.0.0.1:1/` and send → error message in red, no status/elapsed.

Close the window.

- [ ] **Step 4: Commit the VM + view + composition root together**

```bash
git add src/PacketPanda.Desktop/ViewModels/MainWindowViewModel.cs \
        src/PacketPanda.Desktop/Views/MainWindow.axaml \
        src/PacketPanda.Desktop/App.axaml.cs \
        tests/PacketPanda.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat(slice): wire MainWindow to HttpRequestSender via ReactiveCommand"
```

---

## Task 11: Update docs to reflect what actually runs

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`

- [ ] **Step 1: Update `CLAUDE.md`**

In `CLAUDE.md`, find the "## Architecture" section. Leave the existing aspirational description (it still describes the v1 target). Immediately after the architecture section header, insert a new subsection:

```markdown
### Current state (vertical slice)

Only the request-editor + response-viewer flow is implemented. The left "Collections" panel is a placeholder. There is no persistence, no environments, no auth, no scripting, no in-client tests yet — those are separate future tasks per the parent design spec. The one architectural seam that exists is `PacketPanda.Core.Http.IHttpRequestSender`; the ViewModel depends on it and tests mock it with Moq.
```

- [ ] **Step 2: Update `README.md`**

Open `README.md`. Add a "## Status" section near the top (after any existing intro) with:

```markdown
## Status

Vertical slice: a single-window request editor that sends `GET`/`POST`/`PUT`/`DELETE` to a user-entered URL (with optional JSON body) and displays status, timing, and response body. No persistence, collections, environments, auth, or scripting yet — see `docs/superpowers/specs/` for the full design.
```

- [ ] **Step 3: Verify tests still pass**

Run: `dotnet test`
Expected: all tests pass (docs-only change should not break anything).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md README.md
git commit -m "docs(slice): document current vertical-slice state"
```

---

## Task 12: Open the pull request

**Files:**
- None (GitHub PR)

- [ ] **Step 1: Push the branch**

Run:
```bash
git push -u origin task/vertical-slice
```

- [ ] **Step 2: Open the PR**

Run:
```bash
gh pr create --title "feat(slice): vertical slice — send HTTP requests from the UI" --body "$(cat <<'EOF'
## Summary
- Adds `PacketPanda.Core.Http` domain: `HttpMethod`, `HttpRequest`, `HttpResponse`, `SendResult`, `IHttpRequestSender`, `HttpRequestSender`.
- Wires `MainWindowViewModel` to the sender via a `ReactiveCommand` with `canExecute` on the URL.
- Rebuilds the center and right panels of `MainWindow.axaml` as a minimal request editor and response viewer.
- Unit tests for the VM (Moq) and integration tests for the sender (WireMock.Net).

## Test plan
- [ ] `dotnet test` is green.
- [ ] Manual: GET against a real URL shows status/elapsed/body.
- [ ] Manual: POST with a JSON body round-trips through httpbin.
- [ ] Manual: empty URL disables Send.
- [ ] Manual: unreachable host shows a red error, response fields cleared.

Design: `docs/superpowers/specs/2026-04-18-vertical-slice-design.md`
Plan: `docs/superpowers/plans/2026-04-18-vertical-slice.md`
EOF
)"
```

- [ ] **Step 3: Confirm CI passes**

Check the PR page. If CI is green, you're done.

---

## Self-review notes

- Every spec requirement maps to a task: `HttpMethod` → Task 2; records → Task 3; `SendResult` → Task 4; interface → Task 5; sender + error handling → Task 6; VM + canExecute + error mutual-exclusion + body forwarding → Task 8; UI → Task 9; composition root → Task 10; integration tests — four cases from spec + malformed-URL safety net → Task 6; unit tests — five cases from spec → Task 8; docs sync → Task 11.
- No placeholders, no "TODO", no hand-waving — every code step shows the full code.
- Type names stay consistent across tasks (`SendResult.Ok`/`SendResult.Error`, `HttpRequest.Body`, `IHttpRequestSender.SendAsync`).
- Commits are frequent and semantic-formatted per `CLAUDE.md`.
- The deferred items from the spec (cancel, timeout, headers, content-type picker, layering revisit, UI smoke tests) remain deferred — no task here pulls them in.
