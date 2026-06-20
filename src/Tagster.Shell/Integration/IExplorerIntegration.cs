namespace Tagster.Shell;

/// <summary>Registers Tagster's folder right-click commands in the per-user shell (HKCU, no admin).</summary>
public interface IExplorerIntegration
{
    /// <summary>Whether the context-menu commands are currently registered.</summary>
    bool IsRegistered { get; }

    /// <summary>Add "Open in Tagster" and "Edit tags in Tagster" to the folder context menu.</summary>
    void Register();

    /// <summary>Remove the context-menu commands.</summary>
    void Unregister();
}
