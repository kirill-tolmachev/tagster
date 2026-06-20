using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not save settings");
        }
    }
}
