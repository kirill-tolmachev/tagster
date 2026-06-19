namespace Tagster.Core;

/// <summary>A child folder discovered while browsing, with its tag state read from the sidecar.</summary>
public sealed record FolderEntry(string Name, string FullPath, bool IsTagged, IReadOnlyList<string> Tags);
