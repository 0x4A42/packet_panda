# CLAUDE.md

This file provides guidance to Claude Code when working in this repository.

# PacketPanda

A free, lightweight, cross-platform desktop API client for exploring, testing, and debugging HTTP APIs. Requests and environments are stored as JSON files on disk — no database, no export step.

## Stack

- **Language:** C# / .NET 10
- **UI:** Avalonia UI
- **MVVM:** ReactiveUI
- **JSON parsing:** System.Text.Json
- **Syntax highlighting:** AvaloniaEdit
- **JS scripting:** Jint
- **Tests:** xUnit + Moq + FluentAssertions

## Commands

```bash
dotnet build          # build the solution
dotnet run --project src/PacketPanda.Desktop  # run the desktop app
dotnet test           # run all tests
dotnet test --watch   # run tests in watch mode
```

## Architecture

### Current state

**Authentication** is modelled as a discriminated type on the request — one of: None, ApiKey, BearerToken, BasicAuth. The request editor switches its auth sub-panel based on the active type. Auth is injected into the outgoing request at send time (not visible in the Headers tab).

**Environment variables** are resolved at send time: variable references in URLs, headers, and body are substituted from the active environment's key/value map before the HTTP request is built.

**Scripting pipeline** runs per request execution in a sandboxed Jint JS engine: pre-request script → HTTP send → post-request script → tests. Scripts and tests share the same engine instance and context object, which exposes the request, response, and environment read/write API. Post-request scripts can write values back to the active environment (e.g., extract a token from a login response). In-client tests produce a pass/fail list displayed in the response panel, with sample snippets available in the UI.

**Body types** are modelled as a discriminated union (`BodyConfig` in `PacketPanda.Core`): None, JsonBody, PlainTextBody, XmlBody, FormBody (key/value pairs), and MultipartBody (parts with Text or File type). The request editor switches its body sub-panel based on the active mode. File parts in multipart use `IFilePickerService` for the browse dialog.

**Request history** is appended on every send and persisted to disk via `JsonHistoryStore`. Each entry stores the full request + response snapshot. History is capped (default 200, configurable in Settings) and displayed in the history sidebar; clicking an entry opens it as a new unsaved tab.

**Settings** are stored in `AppSettings` (JSON via `JsonSettingsStore`) and cover: theme, SSL certificate verification, follow redirects, request timeout, and history limit. `HttpRequestSender` is rebuilt from `AppSettings` when the user saves settings. The settings modal is opened from the gear icon in the activity bar.

**cURL** — `CurlBuilder` serialises the current in-memory request to a curl command string (Copy as cURL). `CurlParser` parses a curl command string into a `ParsedCurlRequest` record (Import from cURL), which the shell wires up as a new unsaved tab. Both live in `PacketPanda.Core.Http`.

## Testing Conventions

- Tests use **FluentAssertions** for all assertions — `value.Should().Be(...)`, never `Assert.Equal(...)`.
- FluentAssertions is pinned to **7.2.0** (the last Apache-2.0 licensed version).
- Mocks use **Moq** — `new Mock<IFoo>()`, `.Setup(...)`, `.Verify(...)`.
- Test method names follow the `Subject_Scenario_ExpectedOutcome` pattern with PascalCase within each segment — e.g. `Save_Failure_SetsErrorMessageAndLeavesCurrentFilePathUnchanged`.

## Workflow Constraints

- Each functional piece gets its own branch, named `task/<feature-name>` (e.g., `task/add-environments`). Create and checkout the branch before starting work on that piece.
- Commit early and often — commit each completed piece of work, not just at feature completion.
- All PR titles must use semantic commit format: `type(scope): description` — e.g., `feat(auth): add bearer token support`, `fix(env): resolve variable substitution order`. Common types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`.
- Keep documentation in sync with code. When a change affects CLAUDE.md or the README, update those docs in the same unit of work — don't let them drift.

