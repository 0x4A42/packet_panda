# CLAUDE.md

This file provides guidance to Claude Code when working in this repository.

# PacketPanda

A free, lightweight, cross-platform desktop API client for exploring, testing, and debugging HTTP APIs. Requests and environments are stored as TOML files on disk — no database, no export step.

## Stack

- **Language:** C# / .NET 10
- **UI:** Avalonia UI
- **MVVM:** ReactiveUI
- **TOML parsing:** Tomlyn
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

### Current state (vertical slice)

The request-editor + response-viewer flow is implemented, plus a collections sidebar and multi-tab editor. Users can open a folder of `.toml` requests, click a request to open it in a tab, edit and save through Open/Save/Save As, and have their workspace auto-reopen on the next launch. Environments, auth, scripting, and in-client tests remain future work per the parent design spec. Architectural seams in place: `PacketPanda.Core.Http.IHttpRequestSender`, `PacketPanda.Core.Persistence.IRequestFileStore`, `PacketPanda.Core.Persistence.IWorkspaceScanner`, `PacketPanda.Core.Persistence.ISettingsStore`, `PacketPanda.Desktop.Services.IFilePickerService`, `IFolderPickerService`, `IDialogService` — ViewModels depend on these and tests mock them with Moq.

3-panel Avalonia desktop app following ReactiveUI MVVM conventions:

- **Left panel** — Collections sidebar: folders and saved requests loaded from disk
- **Center panel** — Tab bar + request editor: method, URL, body. Multiple requests open in tabs with dirty indicators.
- **Right panel** — Response viewer: syntax-highlighted body, headers, status/timing, per-request history

**Data layer — filesystem only.** There is no database. Collections map to directories; individual requests are `.toml` files inside them. Environments are also `.toml` files stored alongside collections. ViewModels read/write these files directly through an abstracted file service (mockable for tests).

**Reactive core.** ViewModels expose `ReactiveCommand` for sending requests and `[Reactive]` properties for UI state. The async HTTP flow (send → await → update UI) is handled via ReactiveUI's command infrastructure rather than manual async/await wiring in code-behind.

**Authentication** is modelled as a discriminated type on the request — one of: None, ApiKey, BearerToken, BasicAuth, OAuth2. The request editor switches its auth sub-panel based on the active type.

**Environment variables** are resolved at send time: variable references in URLs, headers, and body are substituted from the active environment's key/value map before the HTTP request is built.

**Scripting pipeline** runs per request execution in a sandboxed Jint JS engine: pre-request script → HTTP send → post-request script → tests. Scripts and tests share the same engine instance and context object, which exposes the request, response, and environment read/write API. Post-request scripts can write values back to the active environment (e.g., extract a token from a login response). In-client tests produce a pass/fail list displayed in the response panel, with sample snippets available in the UI.

## Testing Conventions

- Tests use **FluentAssertions** for all assertions — `value.Should().Be(...)`, never `Assert.Equal(...)`. Consistency across the suite beats xUnit's default style.
- FluentAssertions is pinned to **7.2.0** (the last Apache-2.0 licensed version). Do not upgrade to 8.x without discussion — 8.x switched to a commercial licence.
- Mocks use **Moq** — `new Mock<IFoo>()`, `.Setup(...)`, `.Verify(...)`. Do not introduce NSubstitute or other mocking libraries.
- Test method names follow the `Subject_Scenario_ExpectedOutcome` pattern with PascalCase within each segment — e.g. `Save_Failure_SetsErrorMessageAndLeavesCurrentFilePathUnchanged`. No lowercase words between underscores.

## Workflow Constraints

- Each functional piece gets its own branch, named `task/<feature-name>` (e.g., `task/add-environments`). Create and checkout the branch before starting work on that piece.
- Commit early and often — commit each completed piece of work, not just at feature completion.
- Run `dotnet test` before every commit and only commit if all tests pass.
- All PR titles must use semantic commit format: `type(scope): description` — e.g., `feat(auth): add bearer token support`, `fix(env): resolve variable substitution order`. Common types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`.
- Keep documentation in sync with code. When a change affects CLAUDE.md, a design spec under `docs/superpowers/specs/`, or the README, update those docs in the same unit of work — don't let them drift.

## Design Spec

Full design rationale and feature scope: `docs/superpowers/specs/2026-04-17-packet-panda-design.md`
