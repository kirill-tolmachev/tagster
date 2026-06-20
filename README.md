# Tagster

A fast, local folder tagger for a photographers' archive. Browse folders Explorer-style,
tag them, search by tags (include **AND** / exclude **NOT**, Cyrillic + Latin), set folder
covers, and open or tag folders straight from the Windows right-click menu.

## Highlights

- **Tagging & search** — assign multiple tags to folders; filter by several tags at once with
  include/exclude and an instant, Unicode-correct type-to-filter.
- **Portable by design** — each tagged folder holds a tiny hidden `.tagster` sidecar (the
  source of truth), so tags travel with the folder and survive moves, copies, and Windows
  reinstalls. A rebuildable SQLite index in `%AppData%` keeps search fast.
- **Explorer-like browsing** — real shell thumbnails, a virtualized grid, and navigation that
  remembers your scroll position.
- **Folder covers** — set a folder's icon from a photo inside it (or any image), no Photoshop.
- **Explorer integration** — "Open in Tagster" / "Edit tags in Tagster" on folders, toggled
  from Settings (per-user, no admin).

## Requirements

- Windows 10 or 11
- .NET 10 SDK (to build/run from source)

## Build & run

```
dotnet build
dotnet run --project src/Tagster.App
```

Click **Open archive…**, pick the folder that contains your author folders, then tag and
search. Turn on the right-click menu in **Settings**.

## Test

```
dotnet test
```

## Project layout

| Project | What it holds |
|---------|---------------|
| `src/Tagster.Core` | Models, sidecars, tag normalization, query engine, tagging, scanner, navigation — pure C#, fully tested |
| `src/Tagster.Data` | SQLite index (Microsoft.Data.Sqlite + Dapper) |
| `src/Tagster.Shell` | Win32/WPF interop: shell thumbnails, folder covers, Explorer integration |
| `src/Tagster.App` | WPF app (MVVM via CommunityToolkit.Mvvm, Fluent via WPF-UI) |
| `tests/Tagster.Tests` | xUnit suite |

See **[PLAN.md](PLAN.md)** for the architecture and decisions, and
**[docs/PACKAGING.md](docs/PACKAGING.md)** for publishing and the installer.
