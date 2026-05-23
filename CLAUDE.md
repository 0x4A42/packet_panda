# CLAUDE.md

This file provides guidance to Claude Code when working in this repository.

# PacketPanda

A free, lightweight, cross-platform desktop API client for exploring, testing, and debugging HTTP APIs. Collections and environments are stored as plain JSON files on disk — no database, no export step.

## Stack

- **Language:** C# / .NET 10
- **UI:** Avalonia UI
- **MVVM:** ReactiveUI
- **JSON persistence:** System.Text.Json (built-in)
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

**Request history** — every successful or failed send is automatically recorded to `%AppData%/PacketPanda/history.json` (capped at 200 entries). `JsonHistoryStore` manages the file with a `SemaphoreSlim` gate for thread safety. `HistorySidebarViewModel` holds an `ObservableCollection<HistoryItemViewModel>` and exposes `HistoryOpenRequested` for re-open events. `ShellViewModel` subscribes to `RequestEditorViewModel.HistoryEntryRecorded` via `WireHistoryCapture()` and handles re-open via `OpenHistoryEntryInTab`.

**Theming** — light and dark themes are swappable at runtime via the activity-bar toggle. The choice persists across sessions in app settings and defaults to light on first launch.

**Examples** — the `examples/` directory holds sample JSON collections and environments that target a public echo server; they are loadable from the sidebar's open actions.

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

