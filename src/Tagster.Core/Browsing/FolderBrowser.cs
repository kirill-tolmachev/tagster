using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tagster.Core;

/// <inheritdoc />
public sealed class FolderBrowser(ISidecarStore sidecars, ILogger<FolderBrowser>? logger = null) : IFolderBrowser
{
    private readonly ILogger _log = logger ?? NullLogger<FolderBrowser>.Instance;

    public IReadOnlyList<FolderEntry> ListFolders(string path)
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
            DirectoryInfo info;
            try
            {
                info = new DirectoryInfo(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _log.LogDebug(ex, "Skipping unreadable folder {Directory}", directory);
                continue;
            }

            // Hide protected OS folders (hidden + system), e.g. "System Volume Information".
            if (info.Attributes.HasFlag(FileAttributes.Hidden) && info.Attributes.HasFlag(FileAttributes.System))
                continue;

            var tags = sidecars.TryRead(directory)?.Tags ?? [];
            entries.Add(new FolderEntry(info.Name, directory, tags.Count > 0, tags));
        }

        entries.Sort(static (a, b) =>
            string.Compare(a.Name, b.Name, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase));
        return entries;
    }
}
