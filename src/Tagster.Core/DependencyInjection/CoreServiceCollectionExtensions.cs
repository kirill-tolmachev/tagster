using Microsoft.Extensions.DependencyInjection.Extensions;
using Tagster.Core;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI registration for the Tagster core services.</summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddTagsterCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISidecarStore, SidecarStore>();
        services.TryAddSingleton<TaggingService>();
        services.TryAddSingleton<ArchiveScanner>();
        return services;
    }
}
