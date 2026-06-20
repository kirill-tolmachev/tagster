using System.IO;
using System.Text.Json;

namespace Tagster.App;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON; never throws to callers.</summary>
public sealed class SettingsStore
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // fall through to defaults on any read/parse problem
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
        catch
        {
            // best effort
        }
    }
}
