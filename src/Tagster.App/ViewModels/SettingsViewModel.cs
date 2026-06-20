using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Tagster.Shell;

namespace Tagster.App;

/// <summary>Backs the settings window: Explorer integration toggle and startup preferences.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IExplorerIntegration _integration;
    private readonly SettingsStore _settingsStore;
    private readonly ILogger<SettingsViewModel> _log;
    private readonly bool _loaded;

    [ObservableProperty] private bool _explorerMenuEnabled;
    [ObservableProperty] private bool _reopenLastArchive;
    [ObservableProperty] private string? _lastArchivePath;
    [ObservableProperty] private string? _statusMessage;

    public SettingsViewModel(IExplorerIntegration integration, SettingsStore settingsStore, ILogger<SettingsViewModel> logger)
    {
        _integration = integration;
        _settingsStore = settingsStore;
        _log = logger;

        var settings = _settingsStore.Load();
        ExplorerMenuEnabled = _integration.IsRegistered;
        ReopenLastArchive = settings.ReopenLastArchive;
        LastArchivePath = settings.LastArchivePath;
        _loaded = true; // suppress side effects during initial binding
    }

    partial void OnExplorerMenuEnabledChanged(bool value)
    {
        if (!_loaded) return;
        try
        {
            if (value) _integration.Register();
            else _integration.Unregister();
            StatusMessage = value
                ? "Added to the folder right-click menu."
                : "Removed from the folder right-click menu.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to update the Explorer integration");
            StatusMessage = "Couldn't update the menu: " + ex.Message;
        }
    }

    partial void OnReopenLastArchiveChanged(bool value)
    {
        if (!_loaded) return;
        var settings = _settingsStore.Load();
        settings.ReopenLastArchive = value;
        _settingsStore.Save(settings);
    }
}
