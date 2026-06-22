using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tagster.Core;

namespace Tagster.App;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON; never throws to callers.</summary>
public sealed class SettingsStore(ILogger<SettingsStore>? logger = null)
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly ILogger _log = logger ?? NullLogger<SettingsStore>.Instance;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read settings; using defaults");
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            // Atomic + flushed write so a crash mid-save can't truncate settings.json and silently
            // reset every preference on next launch (Load falls back to defaults on a parse error).
            AtomicFile.Write(FilePath, JsonSerializer.Serialize(settings, Options), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not save settings");
        }
    }
}
