# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Tagster is a Windows (10/11) desktop app — a fast, local **folder tagger** for a photographers'
archive. It tags folders, finds them by tag (include/exclude filters, Cyrillic or Latin), gives
them cover images, and adds Explorer right-click integration. Built on **.NET 10** + **WPF**.

## Commands

```sh
dotnet build                                   # builds the whole solution (Tagster.slnx)
dotnet run --project src/Tagster.App           # run the app
dotnet test                                    # full xUnit suite
dotnet test --filter "FullyQualifiedName~SidecarStoreTests"   # one test class
dotnet test --filter "DisplayName~normalizes"                 # by display name substring
```

- The .NET 10 SDK is required and pinned via `global.json` (`rollForward: latestMinor`).
- The solution file is the new XML format `Tagster.slnx` — `dotnet build` with no args finds it.
- Publish / installer: see **docs/PACKAGING.md** (`dotnet publish -c Release -r win-x64
  --self-contained true`, then Inno Setup `iscc installer\Tagster.iss`).

### Headless self-tests (smoke-testing the GUI without a human)

`Tagster.App` honors diagnostic flags that run a check, print a one-line result to the console,
set an exit code, and quit — they bypass single-instance and the main window. Use these to verify
the WPF/Shell layers, which the xUnit suite does **not** cover:

```sh
dotnet run --project src/Tagster.App -- --selftest          # instantiate Main+Settings windows (validates XAML), exit 0
dotnet run --project src/Tagster.App -- --cover-test        # round-trip a folder cover
dotnet run --project src/Tagster.App -- --fileop-test       # create/move/copy/rename/delete via shell IFileOperation
dotnet run --project src/Tagster.App -- --integration-test  # register/unregister the Explorer context menu
dotnet run --project src/Tagster.App -- --log-test          # prove exceptions reach the Serilog file
dotnet run --project src/Tagster.App -- --make-icon [path]  # regenerate Tagster.ico
```

Runtime activation args (from the Explorer menu, parsed in `CommandLine.cs`): `--folder <path>
[--edit]`. `--unregister` is used by the uninstaller.

## Architecture

### One truth, one cache

The defining idea: **sidecars are the source of truth; the SQLite index is a disposable cache.**

1. **Sidecar** — a hidden `.tagster` JSON file written *inside* each tagged folder (`SidecarStore`,
   `Sidecar`). It holds the folder's tags, cover info, a stable GUID identity, and `updatedUtc`.
   Because it lives with the folder, tags survive moves, copies, and OS reinstalls.
2. **Index** — a SQLite DB at `%AppData%\Tagster\index.db` (`SqliteFolderIndex` in `Tagster.Data`,
   implementing `Core.IFolderIndex`) that mirrors the sidecars so search is instant. It is fully
   rebuildable: **Rescan** re-derives it from the sidecars on disk.

**Invariant: never write a sidecar or the index directly from the UI.** All tag writes go through
`TaggingService` (add/remove/set tags, set/clear cover) or `TagManager` (rename/delete across the
archive), which update the sidecar **and** sync the index in one operation. Bypassing them desyncs
the two stores.

### Folder identity is a GUID, not a path

The sidecar's `Id` (a GUID) is the stable identity; the index keys on it. This is what lets a
rename/move re-link to the same index row. `TaggedFolder.RelativePath` is stored relative to the
archive root with `/` separators (see `PathUtil`) so the index is portable across drive letters and
machines. `ArchiveScanner.RescanAsync` reconciles desired-vs-existing by GUID and **re-identifies
duplicate GUIDs** — copy/pasting a tagged folder duplicates its `id`, so the scanner mints a fresh
GUID and rewrites that sidecar.

Sidecar lifecycle edge cases (handled in `TaggingService`/`TagManager`, worth preserving):
- **No tags + no cover** → delete the sidecar and remove from the index.
- **Cover-only** (no tags) → keep the sidecar on disk but remove it from the search index.

### Tag normalization (cross-cutting)

`TagNormalizer.Normalize` = trim + collapse internal whitespace + `ToLowerInvariant()`. Invariant
lowercasing is Unicode-correct for Cyrillic and Latin alike — this is the entire basis for
case-insensitive Cyrillic search, with **no ICU dependency**. The index stores both `tag` (first
display spelling) and `tag_norm`; all matching/grouping happens on the normalized form. Always route
tag comparison through `TagNormalizer`.

### Search semantics live in two mirrored places

`FolderQueryEngine` (pure, in-memory) is the canonical definition of "what matches"; 
`SqliteFolderIndex.SearchAsync` reimplements the same semantics in SQL for the real index. **If you
change matching rules, change both.** Semantics: include tags combine as AND (`TagMatch.All`) or OR
(`Any`); exclude tags are NOT (any match excludes); `NameContains` is a case-insensitive substring.

`FolderQueryEngine.CoOccurringTagCounts` has a second job: given the current result set, it tallies
each normalized tag to how many of those folders carry it (a folder counts once per tag). This drives
the tag panel's **faceted** behavior while a filter is active — `MainViewModel.NarrowTagsTo` hides
tags that can't still combine with the current selection (active tags stay visible so they can be
toggled off) and swaps each tag's shown `Count` for its per-result count; clearing the filter
restores the archive-wide `GlobalCount`.

### Browsing: folders, files, and one display order

`IFolderBrowser.ListEntries` (impl `FolderBrowser`) returns a directory's child folders first — each
carrying the tag state read from its sidecar — then its files (`FolderEntry.IsFile`). **Files are
never tagged**: tagging/cover actions gate on `SelectedIsFolder` rather than just `HasSelection`
(e.g. `TryAddExistingTagAsync`, `RemoveCoverCommand`), so a selected file can't be tagged or given a cover.
The hidden/system visibility rules differ by kind on purpose: a **folder** is hidden only when *both*
Hidden and System are set (so OS folders like "System Volume Information" vanish while ordinary
folders stay), whereas a **file** is hidden when *either* is set — which is exactly what keeps
Tagster's own artifacts out of the grid (the `.tagster` sidecar is hidden-only; the cover files
`.tagster_cover.png` / `Tagster.ico` / `desktop.ini` are hidden+system).

`FolderNameComparer.Default` is the single definition of display order, used by **both** the browser
and the search-results list so the two look identical. It groups by the script of the name's *first
letter* — letterless (digits/symbols) first, then Latin/Western, then Cyrillic — and sorts
culture-aware, case-insensitive within each group; leading non-letters are skipped when classifying
(so "2024-08 Smith" sorts as a Latin name). Like the matching rules, ordering lives in one place —
keep both call sites routed through it.

### Project layers (dependencies point inward)

| Project | TFM | Role |
|---|---|---|
| `Tagster.Core` | `net10.0` | Pure C#, no Windows deps. Models, sidecar I/O, normalization, query engine, `TaggingService`/`TagManager`/`ArchiveScanner`, browsing, navigation. Owns `IFolderIndex` + shell-facing interfaces. |
| `Tagster.Data` | `net10.0` | SQLite index (`Microsoft.Data.Sqlite` + Dapper). Implements `IFolderIndex`. |
| `Tagster.Shell` | `net10.0-windows` | Win32/WPF interop: shell thumbnails, folder covers, shell file operations (`IFileOperation`), Explorer registry integration. |
| `Tagster.App` | `net10.0-windows` (WinExe) | WPF app + composition root. MVVM (CommunityToolkit.Mvvm), generic-host DI, WPF-UI (Fluent). |
| `tests/Tagster.Tests` | `net10.0` | xUnit. References **only Core + Data** — so the tested logic is platform-agnostic; Shell/App are not unit-tested (use the self-tests above). |

`App → {Core, Data, Shell}`; `Data → Core`; `Shell → Core`. Each layer exposes one DI extension in
the `Microsoft.Extensions.DependencyInjection` namespace: `AddTagsterCore()`,
`AddTagsterSqliteIndex(dbPath)`, `AddTagsterShell()`. App's composition root is `App.xaml.cs`
(`OnStartup`): builds a generic host, wires Serilog, registers VMs/windows.

### App-level concerns

- **DI/host**: `Host.CreateApplicationBuilder`; services resolved from `_host.Services`. ViewModels
  and windows are registered there.
- **Threading**: `MainViewModel` runs directory walks, scans, and thumbnail decoding off the UI
  thread via `Task.Run`, marshaling back through a captured `SynchronizationContext`. Thumbnails
  load with bounded concurrency (semaphore of 6) under a `CancellationToken` that is cancelled on
  every navigation. `ImageSource`s are `Freeze()`d so they cross threads safely.
- **View ↔ VM split**: `RelayCommand`s are used where `CanExecute` gates the UI (navigation,
  AddTag, Rescan, RemoveCover). Other interactions (tag include/exclude clicks, rename/delete
  prompts, file/folder dialogs, scroll restore) are wired in `MainWindow.xaml.cs` code-behind, which
  calls `async` VM methods like `ToggleTagAsync`/`RemoveTagAsync`/`SetCoverAsync`.
- **Logging**: Serilog → daily rolling file in `%AppData%\Tagster\logs`, with full stack traces for
  every exception. Global handlers in `App.HookGlobalExceptionHandlers` keep the UI thread alive on
  unhandled UI exceptions (logged + message box) and flush on terminating ones.
- **Single instance**: a second launch hands its args off to the running instance (so an Explorer
  context-menu click activates the existing window) — `SingleInstanceManager` + `App.OnActivated`.

### Windows-specific subtleties

- **File operations** (`FileOperationService`, `IFileOperationService`): copy / cut / paste, rename,
  delete, new-folder, and drag-drop all go through the Windows shell **`IFileOperation`** COM API
  (`Interop/NativeFileOperation.cs`) — so they get Explorer's progress dialog, conflict prompts,
  Recycle Bin, and undo, and interop with Explorer's own clipboard (`CF_HDROP` + the
  `"Preferred DropEffect"` format, see `ClipboardFiles`). These call modal shell UI and must run on
  the UI (STA) thread — do **not** wrap them in `Task.Run`. The owner HWND is supplied by the view via
  `MainViewModel.OwnerWindowProvider`. **Index reconcile:** a structural change never touches the index
  directly — `MainViewModel.AfterStructuralChangeAsync` just reruns **Rescan** when the op was under an
  open archive (tags travel inside the moved/copied folder's sidecar; the scanner re-derives paths and
  re-mints the GUID a copy/paste duplicated). Verify with `--fileop-test`.
- **Folder covers** (`FolderCoverService`): writes a hidden portable `.tagster_cover.png` (source),
  a multi-resolution `Tagster.ico`, and a `desktop.ini`; **marks the folder `System`** (required for
  Windows to honor `desktop.ini`); then calls `SHChangeNotify` so Explorer refreshes. The scanner
  deliberately does **not** skip System folders for this reason.
- **Thumbnail freshness** (`ShellThumbnailService`): prefers a folder's own Tagster cover over the
  shell thumbnail, and decodes with `BitmapCreateOptions.IgnoreImageCache` because the cover file
  name is reused — without it WPF returns a stale previously-decoded bitmap until restart.
- **Explorer integration** (`ExplorerIntegrationService`): per-user **HKCU** registry under
  `Software\Classes\Directory\shell` — no admin. `%1` = clicked folder, `%V` = background folder.

## Conventions

- `Nullable` and `ImplicitUsings` are enabled solution-wide (`Directory.Build.props`); `LangVersion
  latest`. `TreatWarningsAsErrors` is **false**.
- Inject `TimeProvider` for clocks (tests pass `StubTimeProvider`) — do not call
  `DateTimeOffset.UtcNow` directly in Core.
- DI registrations use `TryAdd*` so a host can override any service.
- Sidecar JSON uses short property names (`v`, `id`, `tags`, `cover`, `updatedUtc`) and
  `UnsafeRelaxedJsonEscaping` so Cyrillic is written literally, not as `\uXXXX`. Writes are atomic
  (temp file → `File.Move(overwrite)`), then re-hidden.
- The native SQLite binary is pinned to `SQLitePCLRaw.lib.e_sqlite3 3.50.3` in
  `Tagster.Data.csproj` to dodge a known advisory in the version `Microsoft.Data.Sqlite` pulls
  transitively — read the comment there before changing it.
