# TOML Persistence — Design

Date: 2026-04-19
Parent spec: [`2026-04-17-packet-panda-design.md`](2026-04-17-packet-panda-design.md)
Previous slice: [`2026-04-18-vertical-slice-design.md`](2026-04-18-vertical-slice-design.md)

## Purpose

Add the smallest useful increment of file-based persistence to the vertical slice: save and load a single `HttpRequest` to/from a `.toml` file via OS file picker. Proves the domain ↔ TOML round-trip and settles the file schema before collections, environments, or a workspace tree land.

## Scope

**In:**
- Toolbar row above the existing editor with three buttons: **Open…**, **Save**, **Save As…**
- User-picked file path for every open and save-as
- `CurrentFilePath` tracked on the VM; Save writes to it, or falls back to Save As if null
- TOML schema covers the current `HttpRequest` shape: `method`, `url`, `body` (optional)
- Load failures surface inline via the existing `ErrorMessage` property
- Unit tests for the VM command behaviour (mocked store + picker)
- Round-trip integration tests for the TOML store against real temp files

**Out (deferred):**
- Collections sidebar, workspace directory, recent-files list
- Environments persistence
- Headers, auth, scripts, in-client tests (none exist in the domain yet)
- Unsaved-changes prompt / dirty tracking
- Auto-save
- Multiple requests per file (explicitly chosen; a refactor to a folder-per-file model may happen later per user preference)
- Response persistence or per-request history
- Schema versioning (flat schema now; add a version field when a concrete breaking migration needs it)
- Automated tests for the Avalonia file picker implementation (thin wrapper over `StorageProvider`)

## Architecture

### TOML schema (single request per file)

```toml
method = "GET"
url = "https://example.com/users/42"
body = """
{"hello":"world"}
"""
```

- `method` — string, one of `"GET" | "POST" | "PUT" | "DELETE"`. Written uppercase; read case-insensitively.
- `url` — string, written verbatim. No persistence-time validation; send-time validation still applies.
- `body` — string, optional. Omitted when `HttpRequest.Body` is null or empty. Read back as null when the key is absent or the value is empty.

Unknown top-level keys are silently ignored (keeps files forward-compatible with future additions).

### `PacketPanda.Core`

- `IRequestFileStore`
  - `Task<HttpRequest> LoadAsync(string path)`
  - `Task SaveAsync(string path, HttpRequest request)`
  - Any failure (file I/O, TOML parse, missing required field, unknown verb) surfaces as `RequestFileException`.
- `RequestFileException : Exception` — carries a human-readable `Message`; wraps the underlying exception as `InnerException` where applicable.
- `TomlRequestFileStore : IRequestFileStore`
  - Uses Tomlyn for serialization.
  - On write: uppercases method, omits `body` when null/empty.
  - On read: validates `method` and `url` are present, `method` is one of the four verbs (case-insensitive), returns the resulting `HttpRequest`.

### `PacketPanda.Desktop`

- `IFilePickerService`
  - `Task<string?> PickOpenAsync()` — returns null if the user cancels.
  - `Task<string?> PickSaveAsync()` — returns null if the user cancels.
- `AvaloniaFilePickerService : IFilePickerService`
  - Uses `TopLevel.GetTopLevel(window).StorageProvider` via a constructor-injected window reference.
  - File filter: `*.toml`.
- `MainWindowViewModel` additions:
  - `[Reactive] string? CurrentFilePath`
  - `ReactiveCommand<Unit, Unit> OpenCommand`
  - `ReactiveCommand<Unit, Unit> SaveCommand` — `canExecute`: `Url` non-empty. If `CurrentFilePath` is null, delegates to the Save As flow.
  - `ReactiveCommand<Unit, Unit> SaveAsCommand` — `canExecute`: `Url` non-empty.
  - Depends on injected `IRequestFileStore` and `IFilePickerService` (added alongside the existing `IHttpRequestSender`).
- `App.axaml.cs` composition root constructs `TomlRequestFileStore` and `AvaloniaFilePickerService`, injects them into the VM.
- `MainWindow.axaml` adds a toolbar row above the existing editor with the three buttons bound to the commands.

## Data Flow

### Open

1. User clicks **Open…** → `OpenCommand` fires.
2. VM invokes `_picker.PickOpenAsync()`.
3. Null result (cancelled) → no further action.
4. Path returned → VM invokes `_store.LoadAsync(path)`.
5. Success:
   - VM populates `SelectedMethod`, `Url`, `RequestBody` from the loaded request.
   - VM clears `StatusCode`, `ElapsedMilliseconds`, `ResponseBody`, `ErrorMessage`.
   - VM sets `CurrentFilePath` to the loaded path.
6. `RequestFileException`:
   - VM sets `ErrorMessage` to the exception message.
   - Editor state, response state, and `CurrentFilePath` are left unchanged.

### Save

1. User clicks **Save** → `SaveCommand` fires.
2. If `CurrentFilePath` is null, delegate to Save As flow (pick, then save).
3. Otherwise, VM builds an `HttpRequest` from the current editor state and invokes `_store.SaveAsync(CurrentFilePath, request)`.
4. Success: `ErrorMessage` cleared. Editor state untouched.
5. Failure: `ErrorMessage` set; `CurrentFilePath` unchanged.

### Save As

1. User clicks **Save As…** → `SaveAsCommand` fires.
2. VM invokes `_picker.PickSaveAsync()`.
3. Null (cancelled) → no further action; `CurrentFilePath` unchanged.
4. Path returned → VM invokes `_store.SaveAsync(path, request)`.
5. Success: `CurrentFilePath` updated to the picked path; `ErrorMessage` cleared.
6. Failure: `ErrorMessage` set; `CurrentFilePath` unchanged.

## Error Handling

- All file/parse/validation failures in the store surface as `RequestFileException`. The VM catches only this type and maps `.Message` to `ErrorMessage`.
- Other exceptions (programmer errors, unexpected I/O) propagate.
- The existing `ErrorMessage` channel is reused for load and save failures. It already renders in the response panel; acceptable for this slice because the user just triggered one action and knows what failed. A dedicated error location can be introduced when the UI gets a dedicated status bar.

## Testing

### Unit — `MainWindowViewModelTests` (new cases)

xUnit + Moq + FluentAssertions. Mock `IRequestFileStore` and `IFilePickerService`.

- Open success: picker returns path, store returns request → editor fields populated; response fields and `ErrorMessage` cleared; `CurrentFilePath` set to the picked path.
- Open cancelled: picker returns null → store is never invoked; state unchanged.
- Open failure: store throws `RequestFileException` → `ErrorMessage` matches exception message; editor state and `CurrentFilePath` unchanged.
- Save with `CurrentFilePath` set → store called with that path; picker is not invoked.
- Save with `CurrentFilePath` null → picker invoked, then store called with the picked path; `CurrentFilePath` updated.
- Save with `Url` empty → `SaveCommand.CanExecute` is false.
- Save As always invokes picker regardless of `CurrentFilePath`; on success, `CurrentFilePath` is updated.
- Save As cancelled (picker returns null) → store is not invoked; `CurrentFilePath` unchanged.
- Save failure: store throws → `ErrorMessage` set; `CurrentFilePath` unchanged.

### Unit — `TomlRequestFileStoreTests` (new file)

xUnit + FluentAssertions against real temp files (each test creates and deletes its own temp path).

- Round-trip: save a request, load it back → equal in `Method`, `Url`, `Body`.
- Null body → saved file has no `body` key; loaded request has null body.
- Empty-string body → treated the same as null (omitted on write, null on read).
- Multiline JSON body round-trips correctly.
- Load missing file → `RequestFileException`.
- Load invalid TOML → `RequestFileException`.
- Load TOML missing `method` → `RequestFileException`.
- Load TOML missing `url` → `RequestFileException`.
- Load TOML with unknown verb (e.g. `"PATCH"`) → `RequestFileException`.
- Load TOML with lowercase verb (`"get"`) → succeeds, parses as `HttpMethod.Get`.
- Saved file contains uppercase method regardless of enum name casing.

### Not tested

`AvaloniaFilePickerService` is a thin wrapper over Avalonia's `StorageProvider`. Testing it requires headless UI setup that isn't otherwise used in this slice. Omission is deliberate and noted here so it's not forgotten.

## Follow-up Work (not in this slice)

- Collections sidebar backed by a workspace directory
- Environments persistence (`.toml` files alongside collections)
- Unsaved-changes prompt on Open / app close
- Recent-files list
- Possible refactor to one-file-per-folder with multiple `[[requests]]` entries (user may request later)
- Dedicated status/error area in the UI distinct from the response panel
- Automated tests for the file picker (when headless Avalonia tests land)

Each of these maps to its own `task/<feature-name>` branch per the workflow rules in `CLAUDE.md`.
