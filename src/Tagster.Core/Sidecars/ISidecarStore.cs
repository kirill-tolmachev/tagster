namespace Tagster.Core;

/// <summary>Reads and writes the hidden per-folder <c>.tagster</c> sidecar file.</summary>
public interface ISidecarStore
{
    /// <summary>Read the sidecar in <paramref name="folderPath"/>, or null if absent/unreadable.</summary>
    Sidecar? TryRead(string folderPath);

    /// <summary>Atomically write the sidecar into <paramref name="folderPath"/> and mark it hidden.</summary>
    void Write(string folderPath, Sidecar sidecar);

    /// <summary>Delete the sidecar from <paramref name="folderPath"/> if present.</summary>
    void Delete(string folderPath);
}
