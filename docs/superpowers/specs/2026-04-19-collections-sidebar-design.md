# Collections Sidebar — Design

Date: 2026-04-19
Parent spec: [`2026-04-17-packet-panda-design.md`](2026-04-17-packet-panda-design.md)
Previous slice: [`2026-04-19-toml-persistence-design.md`](2026-04-19-toml-persistence-design.md)

## Purpose

Replace the left-panel placeholder with a live collections tree, add a tab bar to the center panel so multiple requests can be open simultaneously, and persist the last-opened workspace so it reopens automatically on the next launch.

## Decisions

| Question | Decision |
|---|---|
| Workspace persistence | Remember last opened folder; reopen automatically on launch |
| Folder depth | Arbitrary (recursive tree) |
| Tab model | One tab per open file; sidebar click opens new tab or focuses existing |
| Close with unsaved changes | Confirmation dialog — "Unsaved changes. Discard?" |
| New request | `+` button in tab bar → blank unsaved tab; Save As… to place it |
| VM architecture | `ShellViewModel` + `RequestEditorViewModel` per tab + `CollectionSidebarViewModel` |
| Settings location | Platform-standard `AppData/PacketPanda/settings.toml` |
| Tab title | Filename (or `Untitled` for unsaved) + dirty indicator `●` |

## Scope

**In:**
- Left panel: folder tree (arbitrary depth), "Open Folder…" button, click-to-open
- Tab bar above the request editor: `+` new tab, tab titles with dirty indicator, close button per tab
- `RequestEditorViewModel` (rename of `MainWindowViewModel`) — no functional change, just rename
- `ShellViewModel` — owns tab collection, active tab, sidebar VM
- `TabViewModel` — wraps a `RequestEditorViewModel`, exposes title, dirty state, close command
- `CollectionSidebarViewModel` + `CollectionNodeViewModel` tree (folder and request nodes)
- `IWorkspaceScanner` — scans a directory recursively into domain `CollectionNode` tree
- `ISettingsStore` / `TomlSettingsStore` — persists `AppSettings` (just `LastWorkspacePath` for now)
- `IFolderPickerService` / `AvaloniaFolderPickerService` — separate from file picker
- `IDialogService` / `AvaloniaDialogService` — "Unsaved changes, discard?" confirmation
- Startup: load settings, auto-reopen last workspace if path still exists
- Dirty tracking on `RequestEditorViewModel` — compare current state to last-saved snapshot

**Out (deferred):**
- Right-click context menu in sidebar (rename, delete, new request here)
- File system watcher (live refresh when files change on disk)
- Drag-and-drop tab reordering
- Multiple pinned workspaces / recent workspaces list
- Workspace-level search
- Collapsing/expanding all folders at once

## Domain Model

```
CollectionNode (abstract record)
├── Name : string
└── Path : string

FolderNode : CollectionNode
└── Children : IReadOnlyList<CollectionNode>

RequestNode : CollectionNode        // leaf — a .toml file
```

`IWorkspaceScanner.ScanAsync(string rootPath) : Task<FolderNode>` — scans recursively, ignores non-`.toml` files, builds the tree. Lives in `PacketPanda.Core.Persistence`.

## Settings

```toml
last_workspace_path = "C:/Users/jordan/requests"
```

`AppSettings` record — `string? LastWorkspacePath`.

`ISettingsStore`:
- `Task<AppSettings> LoadAsync()`
- `Task SaveAsync(AppSettings settings)`

`TomlSettingsStore : ISettingsStore` — reads/writes `%AppData%/PacketPanda/settings.toml` (via `Environment.GetFolderPath(SpecialFolder.ApplicationData)`). Missing file → returns `new AppSettings()` with null path. Unknown keys silently ignored. Wraps all errors as `SettingsException` (non-fatal — app still starts).

## Architecture

### `PacketPanda.Core`

New namespace: `PacketPanda.Core.Persistence` (already exists).

New files:
- `CollectionNode.cs` — abstract record + `FolderNode` + `RequestNode`
- `IWorkspaceScanner.cs` — interface
- `WorkspaceScanner.cs` — recursive `Directory.GetFileSystemEntries` implementation; folders first, then `.toml` files, alphabetical within each group
- `AppSettings.cs` — `record AppSettings(string? LastWorkspacePath = null)`
- `ISettingsStore.cs` — interface
- `TomlSettingsStore.cs` — Tomlyn implementation; settings file path injected (testable)
- `SettingsException.cs` — non-fatal wrapper

### `PacketPanda.Desktop`

New interfaces/services:
- `IFolderPickerService` — `Task<string?> PickFolderAsync()`
- `AvaloniaFolderPickerService` — uses `window.StorageProvider.OpenFolderPickerAsync`
- `IDialogService` — `Task<bool> ConfirmDiscardAsync(string tabTitle)`
- `AvaloniaDialogService` — uses Avalonia `MessageBoxManager` (or equivalent)

Renamed:
- `MainWindowViewModel` → `RequestEditorViewModel` (no functional change; just the right name now that it's one of many)

New VMs:
- `RequestEditorViewModel` — gains `IsDirty` (bool, reactive); tracks a `_savedSnapshot` (nullable `HttpRequest`) updated on save and load; `IsDirty = current state ≠ snapshot`. The Open/Save/SaveAs commands remain here, scoped to the tab.
- `TabViewModel : ReactiveObject`
  - `RequestEditorViewModel Editor`
  - `string Title` — derived: `IsDirty ? $"{DisplayName} ●" : DisplayName`; `DisplayName` = filename without extension, or `"Untitled"` when `FilePath` is null
  - `bool IsDirty` — forwarded from Editor
  - `string? FilePath` — forwarded from Editor's `CurrentFilePath`
  - `ReactiveCommand<Unit, Unit> CloseCommand` — prompts if dirty via `IDialogService`
- `CollectionNodeViewModel : ReactiveObject`
  - `string Name`
  - `string Path`
  - `bool IsFolder`
  - `ObservableCollection<CollectionNodeViewModel> Children` (empty for request nodes)
  - `bool IsExpanded` (folders only)
- `CollectionSidebarViewModel : ReactiveObject`
  - `ObservableCollection<CollectionNodeViewModel> RootNodes`
  - `string? WorkspacePath`
  - `ReactiveCommand<Unit, Unit> OpenWorkspaceCommand` — picks folder, scans, populates tree, saves path to settings
  - `ReactiveCommand<CollectionNodeViewModel, Unit> OpenRequestCommand` — raises an event/interaction the shell handles
- `ShellViewModel : ReactiveObject`
  - `ObservableCollection<TabViewModel> Tabs`
  - `TabViewModel? ActiveTab`
  - `CollectionSidebarViewModel Sidebar`
  - `ReactiveCommand<Unit, Unit> NewTabCommand` — creates blank `TabViewModel`
  - `ReactiveCommand<string, Unit> OpenFileInTabCommand` — opens path in new tab or focuses existing tab with that path

`App.axaml.cs`:
- On startup: load settings → if `LastWorkspacePath` exists and directory exists, scan and populate sidebar
- Construct `ShellViewModel` as the window's DataContext (replaces `MainWindowViewModel`)

### XAML

`MainWindow.axaml` changes:
- DataContext type → `ShellViewModel`
- Left panel `<Border>`: replace placeholder with `<TreeView>` bound to `Sidebar.RootNodes`, plus "Open Folder…" button bound to `Sidebar.OpenWorkspaceCommand`
- Center panel: wrap existing editor in a `<TabControl>` (or custom tab strip). Tab header template shows `Title` (includes dirty indicator). `+` button beside tabs bound to `NewTabCommand`. Each tab content = the `RequestEditorViewModel` editor UI (extracted into a UserControl or DataTemplate).

## Data Flow

### Opening a workspace

1. User clicks **Open Folder…** in the sidebar.
2. `CollectionSidebarViewModel.OpenWorkspaceCommand` invokes `IFolderPickerService.PickFolderAsync()`.
3. Null (cancelled) → no-op.
4. `IWorkspaceScanner.ScanAsync(path)` → `FolderNode` tree.
5. Tree mapped to `CollectionNodeViewModel` hierarchy → `RootNodes` replaced.
6. `WorkspacePath` updated; `ISettingsStore.SaveAsync(new AppSettings(path))` called.

### Opening a request from the sidebar

1. User clicks a `RequestNode` in the tree.
2. `CollectionSidebarViewModel` raises `OpenRequestCommand` with the file path.
3. `ShellViewModel.OpenFileInTabCommand` checks `Tabs` for an existing tab with that `FilePath`.
4. Found → set `ActiveTab` to that tab.
5. Not found → create new `TabViewModel`, load the file into its `RequestEditorViewModel` via `IRequestFileStore.LoadAsync`, add to `Tabs`, set `ActiveTab`.

### New tab

1. User clicks `+`.
2. `ShellViewModel.NewTabCommand` creates `TabViewModel` with a fresh blank `RequestEditorViewModel`.
3. Appended to `Tabs`, set as `ActiveTab`.
4. `Title` = `"Untitled"`, `IsDirty` = false (nothing typed yet).

### Closing a tab

1. User clicks `×` on a tab.
2. `TabViewModel.CloseCommand` checks `IsDirty`.
3. Not dirty → remove tab from `ShellViewModel.Tabs`. If it was `ActiveTab`, activate the adjacent tab (or null if last tab).
4. Dirty → `IDialogService.ConfirmDiscardAsync(Title)` → Yes: remove. No: cancel.

### Dirty tracking

- On every edit (`SelectedMethod`, `Url`, `RequestBody` change), `RequestEditorViewModel` computes `IsDirty = current ≠ _savedSnapshot`.
- `_savedSnapshot` is set to the current state on successful Save or Load.
- Blank new tab: `_savedSnapshot = null`; `IsDirty = false` until the user types anything, at which point `IsDirty = true` (non-null state ≠ null snapshot).

### Auto-reopen on startup

1. `App.axaml.cs` calls `ISettingsStore.LoadAsync()`.
2. If `LastWorkspacePath` is non-null and `Directory.Exists(path)` → `IWorkspaceScanner.ScanAsync(path)` → populate sidebar. Non-fatal: if scan fails, sidebar starts empty.
3. `ShellViewModel` constructed with pre-populated sidebar.

## Error Handling

- `ISettingsStore` errors → `SettingsException` caught in `App.axaml.cs`, sidebar starts empty, app continues.
- `IWorkspaceScanner` errors → sidebar starts empty, no crash.
- `IRequestFileStore.LoadAsync` errors on sidebar click → `ErrorMessage` on the new tab's editor.
- Inaccessible files in the tree are shown but opening them fails with an error message on the tab.

## Testing

### Unit — `ShellViewModelTests` (new)

Moq `IRequestFileStore`, `IDialogService`.

- `NewTabCommand` → adds a tab, sets it as active, title is `"Untitled"`.
- `OpenFileInTabCommand` with new path → new tab added, file loaded, set as active.
- `OpenFileInTabCommand` with already-open path → no new tab, existing tab focused.
- `CloseCommand` on clean tab → tab removed, adjacent tab activated.
- `CloseCommand` on dirty tab → dialog shown; Yes → removed; No → not removed.
- `CloseCommand` on last tab → tab removed, `ActiveTab` null.

### Unit — `CollectionSidebarViewModelTests` (new)

Moq `IFolderPickerService`, `IWorkspaceScanner`, `ISettingsStore`.

- `OpenWorkspaceCommand` cancelled → no changes.
- `OpenWorkspaceCommand` success → `RootNodes` populated, settings saved.
- `OpenWorkspaceCommand` scan failure → `RootNodes` empty, error surfaced.

### Unit — `WorkspaceScannerTests` (new)

Real temp directory.

- Empty folder → `FolderNode` with no children.
- Folder with `.toml` files → `RequestNode` children, alphabetical.
- Nested subfolders → recursive tree.
- Non-`.toml` files ignored.
- Folders listed before files within each level.

### Unit — `TomlSettingsStoreTests` (new)

Real temp file.

- Missing file → returns `new AppSettings()` (null path).
- Round-trip: save path, load → same path.
- Unknown keys silently ignored on load.

### Unit — `RequestEditorViewModelTests` (renamed from `MainWindowViewModelTests`)

- Add: `IsDirty` false on fresh load.
- Add: `IsDirty` true after editing a field.
- Add: `IsDirty` false after successful save.
- Add: `IsDirty` false after successful open.

### Not tested

- `AvaloniaFolderPickerService` — thin Avalonia wrapper, no headless test.
- `AvaloniaDialogService` — same.
- TreeView XAML binding — covered by manual smoke test.

## Follow-up Work (not in this slice)

- Right-click context menu: new request here, rename, delete
- File system watcher for live sidebar refresh
- Drag-and-drop tab reordering
- Recent workspaces list
- Workspace-level request search
