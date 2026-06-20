using Microsoft.Extensions.DependencyInjection.Extensions;
using Tagster.Shell;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI registration for the Shell (Win32) services.</summary>
public static class ShellServiceCollectionExtensions
{
    public static IServiceCollection AddTagsterShell(this IServiceCollection services)
    {
        services.TryAddSingleton<IThumbnailService, ShellThumbnailService>();
        services.TryAddSingleton<IFolderCoverService, FolderCoverService>();
        services.TryAddSingleton<IExplorerIntegration, ExplorerIntegrationService>();
        return services;
    }
}
