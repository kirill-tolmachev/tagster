using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tagster.Core;

/// <inheritdoc />
public sealed class FolderBrowser(ISidecarStore sidecars, ILogger<FolderBrowser>? logger = null) : IFolderBrowser
{
    private readonly ILogger _log = logger ?? NullLogger<FolderBrowser>.Instance;

    public IReadOnlyList<FolderEntry> ListEntries(string path)
        => [.. ListFolderEntries(path), .. ListFileEntries(path)];

    private IReadOnlyList<FolderEntry> ListFolderEntries(string path)
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            _log.LogDebug(ex, "Could not list folders under {Path}", path);
            return [];
        }

        var entries = new List<FolderEntry>();
        foreach (var directory in directories)
        {
            try
            {
                var info = new DirectoryInfo(directory);

                // Hide protected OS folders (hidden + system), e.g. "System Volume Information".
                if (info.Attributes.HasFlag(FileAttributes.Hidden) && info.Attributes.HasFlag(FileAttributes.System))
                    continue;

                var tags = sidecars.TryRead(directory)?.Tags ?? [];
                entries.Add(new FolderEntry(info.Name, directory, tags.Count > 0, tags));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Reading .Attributes is the first real I/O and can throw on junctions or
                // otherwise inaccessible entries — skip them rather than abort the whole listing.
                _log.LogDebug(ex, "Skipping unreadable folder {Directory}", directory);
            }
        }

        entries.Sort(static (a, b) => FolderNameComparer.Default.Compare(a.Name, b.Name));
        return entries;
    }

    private IReadOnlyList<FolderEntry> ListFileEntries(string path)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            _log.LogDebug(ex, "Could not list files under {Path}", path);
            return [];
        }

        var entries = new List<FolderEntry>();
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);

                // Skip hidden OR system files — matches Explorer's default view and, crucially, keeps
                // Tagster's own artifacts out of sight: the .tagster sidecar is hidden-only, while the
                // cover files (.tagster_cover.png, Tagster.ico, desktop.ini) are hidden+system.
                if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System))
                    continue;

                entries.Add(new FolderEntry(info.Name, file, IsTagged: false, Tags: [], IsFile: true));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // A reserved device name like "nul"/"con" survives enumeration but throws
                // "The parameter is incorrect" when its .Attributes are read — skip such entries
                // (and any other unreadable file) instead of letting the listing crash.
                _log.LogDebug(ex, "Skipping unreadable file {File}", file);
            }
        }

        entries.Sort(static (a, b) => FolderNameComparer.Default.Compare(a.Name, b.Name));
        return entries;
    }
}
