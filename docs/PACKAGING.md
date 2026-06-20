# Packaging Tagster

## 1. Publish the app

Self-contained (no .NET needed on the target machine — simplest for end users):

```
dotnet publish src/Tagster.App/Tagster.App.csproj -c Release -r win-x64 --self-contained true
```

Output: `src/Tagster.App/bin/Release/net10.0-windows/win-x64/publish` (contains `Tagster.exe`).

For a much smaller, framework-dependent build, drop `--self-contained true`; users then
need the **.NET 10 Desktop Runtime** installed. WPF single-file publishing is not
recommended here — ship the publish folder.

## 2. Build the installer (optional)

1. Install [Inno Setup 6+](https://jrsoftware.org/isinfo.php).
2. Compile the script (point it at the publish folder if it differs):

```
iscc installer\Tagster.iss
```

The setup executable is written to `installer/dist`. It installs per-user (no admin),
adds Start Menu (and optional desktop) shortcuts.

## Explorer right-click menu

Tagster registers **Open in Tagster** / **Edit tags in Tagster** per-user (HKCU) from
**Settings** inside the app — no admin rights and no installer registry changes. Turn it
off in Settings before uninstalling to remove the entries.
