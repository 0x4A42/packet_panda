# Vertical Slice — Design

Date: 2026-04-18
Parent spec: [`2026-04-17-packet-panda-design.md`](2026-04-17-packet-panda-design.md)

## Purpose

Prove the Avalonia + ReactiveUI + HttpClient loop works end-to-end with the thinnest useful feature set. Every subsequent feature in the parent spec (collections, environments, auth, scripting, tests) is an additive layer on top of this slice rather than a speculative integration.

## Scope

**In:**
- Single window, single view
- Method dropdown: `GET`, `POST`, `PUT`, `DELETE`
- URL textbox
- Raw-body multiline textbox (single content type: `application/json` when non-empty)
- Send button
- Response panel showing status code, elapsed milliseconds, and raw body text
- Network-level errors rendered as a plain error string in the response panel
- Unit tests for the ViewModel
- Integration tests for the HTTP sender against a local WireMock.Net server

**Out (deferred, noted as follow-ups):**
- Request headers UI
- Content-type picker / non-JSON bodies
- Cancellation button
- Configurable timeout
- Collections, persistence, environments, authentication, scripting, in-client tests
- Syntax highlighting of the response body
- Response headers display
- Response history
- Headless Avalonia UI smoke tests
- Full layered architecture (DI container, service folders) — revisit when features grow

## Architecture

Two projects, one seam between them. The seam is `IHttpRequestSender` — the only abstraction introduced in this slice. Keeps the ViewModel unit-testable without mocking `HttpClient` directly.

### `PacketPanda.Core`

- `HttpMethod` enum — `Get`, `Post`, `Put`, `Delete`
- `HttpRequest` record — `Method`, `Url`, `Body` (nullable string)
- `HttpResponse` record — `StatusCode` (int), `Body` (string), `ElapsedMilliseconds` (long)
- `SendResult` — discriminated result representing either `HttpResponse` (success, including non-2xx) or `SendError(string Message)` (network-level failure). Exact shape (sealed hierarchy vs. record with nullable fields) to be decided in the implementation plan — the contract is what matters.
- `IHttpRequestSender` — `Task<SendResult> SendAsync(HttpRequest request)`
- `HttpRequestSender : IHttpRequestSender` — wraps a single long-lived `HttpClient`. When `HttpRequest.Body` is non-null/non-empty, attaches it as a `StringContent` with `Content-Type: application/json`. Times the round trip with `Stopwatch`. Catches:
  - `HttpRequestException` — DNS, connection refused, TLS failures
  - `TaskCanceledException` — default `HttpClient` timeout firing
  - `UriFormatException` / `InvalidOperationException` — malformed URL
  All other exceptions propagate.

### `PacketPanda.Desktop`

- `MainWindow.axaml` + `MainWindow.axaml.cs` — the single view
- `MainWindowViewModel : ReactiveObject`:
  - `[Reactive]` properties: `SelectedMethod`, `Url`, `RequestBody` (nullable), `StatusCode`, `ElapsedMilliseconds`, `ResponseBody`, `ErrorMessage`
  - `ReactiveCommand<Unit, Unit> SendCommand` — built via `ReactiveCommand.CreateFromTask` with a `canExecute` observable: `Url` non-empty AND command not currently executing
  - Depends on injected `IHttpRequestSender`
- `App.axaml.cs` — composition root. Constructs one `HttpClient`, one `HttpRequestSender`, one `MainWindowViewModel`; wires them into the window's `DataContext`. No DI container yet.

### Deferred architectural work

Note for future refactor (not in this slice): once persistence, environments, auth, and scripting arrive, revisit the layering. Likely changes include a `Services/` folder in `Core`, a `ViewModels/` folder in `Desktop`, and potentially a DI container if composition sprawls.

## Data Flow

1. User picks method from dropdown → `SelectedMethod` updates
2. User types URL → `Url` updates
3. User optionally fills the body textbox → `RequestBody` updates
4. User clicks Send → `SendCommand.Execute()` fires
5. ViewModel clears `ErrorMessage`, builds `HttpRequest` from `SelectedMethod` + `Url` + `RequestBody`, awaits `_sender.SendAsync(request)`
6. `HttpRequestSender` starts a `Stopwatch`, calls `HttpClient.SendAsync`, reads body as string, stops the stopwatch, returns either `HttpResponse` or `SendError`
7. ViewModel receives `SendResult`:
   - `HttpResponse` → populates `StatusCode`, `ElapsedMilliseconds`, `ResponseBody`; leaves `ErrorMessage` cleared
   - `SendError` → populates `ErrorMessage`; clears `StatusCode`, `ElapsedMilliseconds`, `ResponseBody`
8. Bindings refresh the response panel

Non-2xx responses are treated as normal `HttpResponse` values with no special styling — status code and body display as-is.

## Error Handling

- Network-level failures (listed in the sender section) are caught and converted to `SendError` with `ex.Message` as the text
- Non-2xx responses are not errors — they are normal responses
- Programmer errors and unexpected exceptions propagate (no blanket `catch (Exception)`)
- ViewModel enforces mutual exclusion between `ErrorMessage` and the three response fields: whichever path wins, the other clears

## Testing

### Unit — `MainWindowViewModelTests`

xUnit + Moq + FluentAssertions. Mock `IHttpRequestSender`.

- Sending with a valid URL: sender invoked with a matching `HttpRequest`; `StatusCode`, `ElapsedMilliseconds`, `ResponseBody` populated from the returned `HttpResponse`
- Sender returns `SendError`: `ErrorMessage` populated; response fields cleared
- `SendCommand.CanExecute` is false when `Url` is empty
- `SendCommand.CanExecute` is false while a send is in flight
- Non-null, non-empty `RequestBody` is forwarded on the `HttpRequest`; null/empty stays null

### Integration — `HttpRequestSenderTests`

xUnit + FluentAssertions against a local `WireMock.Net` server.

- 200 response with JSON body → `HttpResponse` with status 200, body string matches, `ElapsedMilliseconds` > 0
- 404 response with body → `HttpResponse` with status 404 (treated as normal, not `SendError`)
- Request to an unreachable port → `SendError`
- POST with a JSON body → WireMock receives the body text and a `Content-Type: application/json` header

No headless Avalonia UI tests in this slice.

## Follow-up Work (not in this slice)

Tracked here so the slice's deliberate omissions aren't forgotten:

- Request headers editor
- Content-Type selector and non-JSON body support
- Cancel button on in-flight requests
- Configurable per-request timeout
- Revisit the `Core`/`Desktop` layering once features grow
- Headless Avalonia UI smoke tests

Each of these maps to its own `task/<feature-name>` branch per the workflow rules in `CLAUDE.md`.
