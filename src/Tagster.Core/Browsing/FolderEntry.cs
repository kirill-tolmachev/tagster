namespace Tagster.Core;

/// <summary>
/// An item discovered while browsing a directory — a child folder or a file. Tag state is read from
/// the sidecar for folders; files are never tagged (<see cref="IsFile"/> is true, <see cref="Tags"/> empty).
/// </summary>
public sealed record FolderEntry(string Name, string FullPath, bool IsTagged, IReadOnlyList<string> Tags, bool IsFile = false);
