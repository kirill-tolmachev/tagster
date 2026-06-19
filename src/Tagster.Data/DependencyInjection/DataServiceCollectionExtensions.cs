using Microsoft.Extensions.DependencyInjection.Extensions;
using Tagster.Core;
using Tagster.Data;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>DI registration for the SQLite-backed folder index.</summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>Register <see cref="IFolderIndex"/> backed by a SQLite database at the given path.</summary>
    public static IServiceCollection AddTagsterSqliteIndex(this IServiceCollection services, string databasePath)
    {
        services.TryAddSingleton<IFolderIndex>(_ => new SqliteFolderIndex(databasePath));
        return services;
    }
}
