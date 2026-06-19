# Tagster — Implementation Plan

A simple, fast, local folder cataloguer for a photographers' archive: assign tags to
folders, search by include/exclude tags, browse Explorer-style with thumbnails, and set
folder covers from inside the app. Windows 10 & 11, fully Unicode (Cyrillic + Latin).

**Status:** M1–M3 complete (headless core, browser shell, tagging + search UI); M4 (folder covers) next.
**Decisions locked:** 2026-06-19.

---

## 1. Locked decisions

| Area | Decision |
|------|----------|
| UI stack | **WPF on .NET 10 (LTS), unpackaged**, styled with WPF-UI (Fluent). MVVM via `CommunityToolkit.Mvvm`, DI via `Microsoft.Extensions.Hosting`. |
| Tag storage | **Hybrid** — a tiny hidden per-folder sidecar (`.tagster`) is the source of truth; a **rebuildable SQLite index** powers fast search. |
| Explorer integration | **Lightweight folder right-click menu** ("Open in Tagster", "Edit tags…"). Standalone app is the primary UI. |
| Tag model | **Flat list** with instant type-to-filter; click to include, Alt-click to exclude. |

### Open assumption (cost-free superset — flag to simplify)
Designed for **one or more archive roots, any folder taggable at any depth**, with the
default landing view = a root's direct child folders ("author" folders). If tagging is
only ever applied to those top-level folders under a single root, the model can be trimmed.

---

## 2. The bar: beat Tag Explorer

These are hard acceptance criteria, derived from the current tool's failures.

| Tag Explorer flaw | Root cause | Tagster fix |
|---|---|---|
| Cyrillic tag search broken (checkboxes only) | ASCII-only handling; SQLite `NOCASE` is ASCII-only | Normalized lowercase keys (`ToLowerInvariant`, Unicode-correct) + live **type-to-filter** |
| Lags past 2 selected tags | Naive filtering, no index/virtualization | Indexed set-intersection (SQL + in-memory) feeding a **virtualized** tile grid |
| Cannot exclude tags | No set-difference | First-class **include AND / exclude NOT**, native to the model |
| "Back" jumps to top of list | View state discarded on navigation | Navigation stack stores **scroll offset + selection** per view |

---

## 3. Architecture & repository layout

Logic is isolated from UI and Win32 so the core is fully unit-testable.

```
Tagster.sln
├─ src/
│  ├─ Tagster.Core    — domain models, sidecar I/O, tag normalization,
│  │                     tagging service, scan/reconcile, query engine. Pure C#.
│  ├─ Tagster.Data    — SQLite index (Microsoft.Data.Sqlite + Dapper),
│  │                     implements Tagster.Core.IFolderIndex.
│  ├─ Tagster.Shell   — [M2] Win32/COM via CsWin32: Explorer thumbnails,
│  │                     cover/desktop.ini + .ico generation, SHChangeNotify.
│  ├─ Tagster.App     — [M2] WPF shell (MVVM, DI, virtualized grid, tag panel).
│  └─ Tagster.ShellMenu — [M5] context-menu registration (registry verbs;
│                          optional sparse-package IExplorerCommand later).
└─ tests/
   └─ Tagster.Tests   — xUnit over Core + Data.
```

`net10.0` for libraries; `net10.0-windows` for `Tagster.Shell` / `Tagster.App`.

---

## 4. Data model

### Sidecar — `.tagster` (hidden, one per tagged folder; the source of truth)
```json
{
  "v": 1,
  "id": "8d3f…-guid",
  "tags": ["док", "портрет"],
  "cover": { "source": ".tagster_cover.jpg", "setUtc": "2026-06-19T17:00:00Z" },
  "updatedUtc": "2026-06-19T17:00:00Z"
}
```
The stable `id` (GUID) lets a **renamed or moved folder keep its tags** — reconciliation
matches on identity, not path. Written atomically (temp + replace) and marked hidden.
Cyrillic is stored literally (relaxed JSON encoder), not `\uXXXX`.

### Root catalog — `tags.json` (per archive root) — [M3]
Canonical tag names (+ optional colour/usage) so renames and cosmetics travel and restore.

### SQLite index (in `%AppData%`, disposable & rebuildable)
```
folders(id PK, root_path, relative_path, name, name_norm, updated_utc,
        UNIQUE(root_path, relative_path))
folder_tags(folder_id FK→folders ON DELETE CASCADE, tag, tag_norm,
        PRIMARY KEY(folder_id, tag_norm))
indexes: folder_tags(tag_norm), folder_tags(folder_id), folders(root_path)
```
`name_norm`/`tag_norm` are pre-lowercased (`ToLowerInvariant`) so Cyrillic search and
`LIKE` matching are case-insensitive without ICU. WAL journal mode.

### Search semantics
`include` (AND or ANY) ∩ folders, minus any folder carrying an `exclude` tag, optionally
filtered by a normalized substring of the folder name. Example: `has {док} AND NOT {война}`.

---

## 5. Portability (core requirement)

- **Move to another PC:** copy the archive → open Tagster → add the root → scan rebuilds
  everything from the per-folder sidecars + `tags.json`.
- **After a Windows reinstall:** reinstall app → add root → scan. The `%AppData%` index is
  disposable; nothing separate to back up.
- Sidecar always wins on conflict (clean story if synced via Dropbox/cloud).
- Copy/paste of a tagged folder duplicates its GUID → the scanner **detects and
  re-identifies** duplicates (assigns a fresh GUID, rewrites the sidecar).

---

## 6. Hard Windows bits (M2+)

- **Explorer-quality thumbnails:** `IShellItemImageFactory::GetImage` (via CsWin32) on
  background threads → the exact images Explorer shows, with an async LRU cache keyed by
  path+mtime+size and offscreen cancellation. Grid uses a virtualizing wrap panel.
- **Cover feature:** pick a photo from inside the folder *or* drop an external image →
  crop/scale → generate a multi-resolution `.ico` (16–256px) → write `desktop.ini`
  (`IconResource`), set folder `+s` and ini `+h +s`, then `SHChangeNotify` to refresh. The
  source image is kept as hidden `.tagster_cover.*` so the cover travels and regenerates.

---

## 7. Explorer integration

- **v1:** classic registry verbs under `HKCU\Software\Classes\Directory\shell\Tagster…`
  → launch the single-instance app with `--folder "<path>" [--edit]`. Works on Win10
  (normal menu) and Win11 (under "Show more options").
- **v1.1 (optional):** a sparse MSIX package providing `IExplorerCommand` to surface the
  command in the Win11 *main* menu while the app itself stays unpackaged.

---

## 8. Milestones

- [x] **M1 — Skeleton & data (headless, tested):** solution, DI, SQLite index, sidecar I/O,
      scan/reconcile, tag normalization, query engine.
- [x] **M2 — Browse & thumbnails:** virtualized grid, Explorer thumbnails, navigation with
      preserved scroll, breadcrumb.
- [x] **M3 — Tagging & search:** tag panel (type-to-filter, include/exclude), search engine,
      assign/remove, tag management (rename/merge/delete propagated to sidecars).
- [ ] **M4 — Covers:** set from inside-folder photo or external image; `.ico` + `desktop.ini`
      automation + refresh.
- [ ] **M5 — Explorer menu & installer:** registry verbs, single-instance activation,
      Inno Setup/MSI. Optional sparse package.
- [ ] **M6 — Polish:** perf pass at thousands of folders, backup/export, settings, logging.

---

## 9. Risks / watch-items

- Windows **icon-cache staleness** after setting covers → `SHChangeNotify` + per-folder ini.
- `desktop.ini` attribute correctness (test on Win10 *and* 11). Cover-bearing folders become
  system folders — the scanner must **not** skip system folders (only reparse points).
- Sidecar writes need a **writable** archive (detect read-only/network; warn).
- Thumbnail throughput under load → throttle + cache + cancel offscreen requests.
- Deep recursive scans → bounded depth + skip reparse points; optimize incrementally later.
