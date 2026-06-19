using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tagster.Core;

/// <inheritdoc />
public sealed class SidecarStore : ISidecarStore
{
    /// <summary>The fixed sidecar file name written into each tagged folder.</summary>
    public const string FileName = ".tagster";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Write Cyrillic (and other non-ASCII) literally rather than as \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public Sidecar? TryRead(string folderPath)
    {
        var path = Path.Combine(folderPath, FileName);
        if (!File.Exists(path)) return null;

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<Sidecar>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(string folderPath, Sidecar sidecar)
    {
        Directory.CreateDirectory(folderPath);
        var path = Path.Combine(folderPath, FileName);
        var temp = Path.Combine(folderPath, $"{FileName}.{Guid.NewGuid():N}.tmp");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(sidecar, JsonOptions);
        File.WriteAllBytes(temp, bytes);

        // Clear attributes on any existing (hidden) target so the replace cannot fail on Windows.
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.Normal);
        File.Move(temp, path, overwrite: true);

        TrySetHidden(path);
    }

    public void Delete(string folderPath)
    {
        var path = Path.Combine(folderPath, FileName);
        if (!File.Exists(path)) return;
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    private static void TrySetHidden(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The hidden attribute is cosmetic; ignore on filesystems that don't support it.
        }
    }
}
