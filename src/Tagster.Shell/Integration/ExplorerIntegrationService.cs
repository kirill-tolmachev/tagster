using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Tagster.Shell;

/// <inheritdoc />
public sealed class ExplorerIntegrationService(ILogger<ExplorerIntegrationService>? logger = null) : IExplorerIntegration
{
    private readonly ILogger _log = logger ?? NullLogger<ExplorerIntegrationService>.Instance;

    private const string FolderShell = @"Software\Classes\Directory\shell\";
    private const string BackgroundShell = @"Software\Classes\Directory\Background\shell\";
    private const string OpenVerb = "Tagster.Open";
    private const string EditVerb = "Tagster.EditTags";

    public bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(FolderShell + OpenVerb);
            return key is not null;
        }
    }

    public void Register()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the application executable path.");

        // %1 = the clicked folder; %V = the folder whose background was clicked.
        WriteVerb(FolderShell + OpenVerb, "Open in Tagster", exe, $"\"{exe}\" --folder \"%1\"");
        WriteVerb(FolderShell + EditVerb, "Edit tags in Tagster", exe, $"\"{exe}\" --folder \"%1\" --edit");
        WriteVerb(BackgroundShell + OpenVerb, "Open in Tagster", exe, $"\"{exe}\" --folder \"%V\"");
    }

    public void Unregister()
    {
        Delete(FolderShell + OpenVerb);
        Delete(FolderShell + EditVerb);
        Delete(BackgroundShell + OpenVerb);
    }

    private static void WriteVerb(string keyPath, string label, string iconPath, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(null, label);
        key.SetValue("Icon", iconPath);
        using var commandKey = key.CreateSubKey("command");
        commandKey.SetValue(null, command);
    }

    private void Delete(string keyPath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _log.LogWarning(ex, "Could not remove shell registry key {Key}", keyPath);
        }
    }
}
