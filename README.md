# Tagster

A fast, local **folder tagger** for a photographers' archive on Windows 10/11. Browse your
author folders Explorer-style, tag them, find them by tag (including *exclude* filters, in
Cyrillic or Latin), give them cover images, and open or tag folders straight from the Windows
right‑click menu — all backed by a portable, file-based store.

---

## Features

- **Tagging & search.** Assign as many tags as you like to a folder. Filter by several tags at
  once: click **＋** to *include* (must have) and **−** to *exclude* (must not have). A live,
  Unicode-correct **type-to-filter** box narrows the tag list as you type, and an *active
  filters* bar shows exactly what's applied.
- **Portable by design.** Every tagged folder carries a tiny hidden **`.tagster`** sidecar that
  is the source of truth, so tags travel with the folder and survive moves, copies, and even a
  Windows reinstall. A rebuildable **SQLite index** in `%AppData%` keeps search instant — lose
  it and it's recreated by re-scanning.
- **Explorer-like browsing.** Real Windows shell thumbnails, a virtualized grid that stays
  smooth with thousands of folders, a breadcrumb path, and navigation that **remembers your
  scroll position** when you go back.
- **Folder covers.** Pick a photo from inside a folder (or any image) and set it as the folder's
  cover icon — no Photoshop, no fiddling with folder properties. It updates in the app and in
  Explorer, instantly.
- **Windows Explorer integration.** Adds **“Open in Tagster”** and **“Edit tags in Tagster”** to
  a folder's right‑click menu. Per-user, no admin — toggle it in **Settings**.
- **Modern Fluent UI.** A clean three-pane layout (filter · browse · inspect) that follows your
  Windows **light/dark theme** and accent color.

## How it works

Tagster keeps two things in sync:

1. **The sidecars — the truth.** A hidden `.tagster` JSON file inside each tagged folder holds
   its tags and cover info. Because it lives *with* the folder, your tags are as portable as the
   archive itself.
2. **The index — for speed.** A SQLite database (in `%AppData%\Tagster`) mirrors the sidecars so
   searching across hundreds of folders is instant. It's disposable: **Rescan** rebuilds it from
   the sidecars at any time.

Move the archive to a new PC, or reinstall Windows, and you only need to point Tagster at the
folder again — the tags are already there.

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build and run from source

## Getting started

```sh
dotnet build
dotnet run --project src/Tagster.App
```

Then:

1. Click **Open** and choose the folder that contains your author folders. Tagster scans it.
2. **Tag a folder** — select a tile; in the right-hand inspector, type a tag and press Enter (or
   click ＋). Remove a tag with the **×** on its chip.
3. **Find folders** — in the left panel, click a tag's **＋** to require it or **−** to exclude
   it. The grid updates to matching folders; clear filters to go back to browsing.
4. **Set a cover** — with a folder selected, click **Set cover…** and pick an image (the picker
   opens inside that folder, so a photo from it is one click away).
5. **Open in Explorer** — the inspector's button opens the selected folder in Windows Explorer.

Turn on the right-click menu any time from **Settings**.

## Tests

```sh
dotnet test
```

The Core and Data layers are covered by an xUnit suite (sidecars, tag normalization, the
include/exclude query engine, the SQLite index, the scanner's identity reconciliation, tag
rename/merge/delete, and an end-to-end scan→search→manage flow).

## Packaging

See **[docs/PACKAGING.md](docs/PACKAGING.md)** for publishing a self-contained build and building
the per-user installer with Inno Setup.

## Logs

Tagster writes a daily rolling log to `%AppData%\Tagster\logs\tagster-<date>.log` (Serilog),
with full stack traces for **every** exception — handled or unhandled. Attach the latest file
when reporting a problem.

## Project layout

| Project | What it holds |
|---------|---------------|
| `src/Tagster.Core` | Models, sidecar I/O, tag normalization, query engine, tagging, scanner, navigation, browsing — pure C#, fully tested |
| `src/Tagster.Data` | SQLite index (Microsoft.Data.Sqlite + Dapper) |
| `src/Tagster.Shell` | Win32/WPF interop: shell thumbnails, folder covers, Explorer integration |
| `src/Tagster.App` | WPF app — MVVM (CommunityToolkit.Mvvm), generic-host DI, Fluent UI (WPF-UI) |
| `tests/Tagster.Tests` | xUnit suite |

See **[PLAN.md](PLAN.md)** for the architecture and the decisions behind it.
