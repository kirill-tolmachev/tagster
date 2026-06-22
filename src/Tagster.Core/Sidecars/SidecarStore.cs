using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tagster.Core;

/// <inheritdoc />
public sealed class SidecarStore(ILogger<SidecarStore>? logger = null) : ISidecarStore
{
    /// <summary>The fixed sidecar file name written into each tagged folder.</summary>
    public const string FileName = ".tagster";

    private readonly ILogger _log = logger ?? NullLogger<SidecarStore>.Instance;

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
            var sidecar = JsonSerializer.Deserialize<Sidecar>(stream, JsonOptions);
            // A "tags": null in the JSON overwrites the [] initializer, leaving Tags null and
            // NRE-ing every downstream consumer. Coalesce here so all readers are protected.
            return sidecar is null ? null : sidecar with { Tags = sidecar.Tags ?? [] };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Unreadable or corrupt sidecar in {Folder}", folderPath);
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

    private void TrySetHidden(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The hidden attribute is cosmetic; log and carry on.
            _log.LogDebug(ex, "Could not set hidden attribute on {Path}", path);
        }
    }
}
