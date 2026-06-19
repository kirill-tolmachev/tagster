using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Tagster.Core;

namespace Tagster.Data;

/// <summary>
/// SQLite-backed <see cref="IFolderIndex"/>. The database is a disposable, rebuildable cache (the
/// sidecars are the source of truth). Normalized columns make Cyrillic search and <c>LIKE</c>
/// case-insensitive without needing ICU.
/// </summary>
public sealed class SqliteFolderIndex : IFolderIndex, IDisposable
{
    private readonly string _connectionString;

    public SqliteFolderIndex(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();

        EnsureSchema();
    }

    public async Task UpsertAsync(TaggedFolder folder, CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var id = folder.Id.ToString();

        // Keep the UNIQUE(root_path, relative_path) constraint clean if another id sat at this path.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM folders WHERE root_path = @Root AND relative_path = @Rel AND id <> @Id;",
            new { Root = folder.RootPath, Rel = folder.RelativePath, Id = id },
            transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO folders (id, root_path, relative_path, name, name_norm, updated_utc)
            VALUES (@Id, @Root, @Rel, @Name, @NameNorm, @Updated)
            ON CONFLICT(id) DO UPDATE SET
                root_path = excluded.root_path,
                relative_path = excluded.relative_path,
                name = excluded.name,
                name_norm = excluded.name_norm,
                updated_utc = excluded.updated_utc;
            """,
            new
            {
                Id = id,
                Root = folder.RootPath,
                Rel = folder.RelativePath,
                Name = folder.Name,
                NameNorm = folder.Name.ToLowerInvariant(),
                Updated = folder.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture),
            },
            transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM folder_tags WHERE folder_id = @Id;",
            new { Id = id }, transaction, cancellationToken: cancellationToken));

        var tagRows = folder.Tags
            .Select(t => new { Norm = TagNormalizer.Normalize(t), Display = t })
            .Where(t => t.Norm.Length > 0)
            .GroupBy(t => t.Norm, StringComparer.Ordinal)
            .Select(g => new { FolderId = id, Tag = g.First().Display, TagNorm = g.Key })
            .ToList();

        if (tagRows.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT OR IGNORE INTO folder_tags (folder_id, tag, tag_norm) VALUES (@FolderId, @Tag, @TagNorm);",
                tagRows, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM folders WHERE id = @Id;",
            new { Id = id.ToString() }, cancellationToken: cancellationToken));
    }

    public async Task<TaggedFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        var rows = await QueryFoldersAsync(connection,
            $"SELECT {FolderColumns} FROM folders WHERE id = @Id;",
            new { Id = id.ToString() }, cancellationToken);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<IReadOnlyList<TaggedFolder>> GetAllAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        return await QueryFoldersAsync(connection,
            $"SELECT {FolderColumns} FROM folders WHERE root_path = @Root;",
            new { Root = rootPath }, cancellationToken);
    }

    public async Task<IReadOnlyList<TaggedFolder>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var include = query.Include.Select(TagNormalizer.Normalize).Where(n => n.Length > 0).Distinct().ToList();
        var exclude = query.Exclude.Select(TagNormalizer.Normalize).Where(n => n.Length > 0).Distinct().ToList();
        var name = string.IsNullOrWhiteSpace(query.NameContains) ? null : query.NameContains.Trim().ToLowerInvariant();

        var parameters = new DynamicParameters();

        string baseIds;
        if (include.Count > 0)
        {
            parameters.Add("Include", include);
            if (query.IncludeMatch == TagMatch.All)
            {
                parameters.Add("IncludeCount", include.Count);
                baseIds = """
                    SELECT folder_id FROM folder_tags WHERE tag_norm IN @Include
                    GROUP BY folder_id HAVING COUNT(DISTINCT tag_norm) = @IncludeCount
                    """;
            }
            else
            {
                baseIds = "SELECT DISTINCT folder_id FROM folder_tags WHERE tag_norm IN @Include";
            }
        }
        else
        {
            baseIds = "SELECT id FROM folders";
        }

        var sql = $"SELECT {FolderColumns} FROM folders WHERE id IN ({baseIds})";

        if (exclude.Count > 0)
        {
            parameters.Add("Exclude", exclude);
            sql += " AND id NOT IN (SELECT folder_id FROM folder_tags WHERE tag_norm IN @Exclude)";
        }

        if (name is not null)
        {
            parameters.Add("Name", "%" + EscapeLike(name) + "%");
            sql += " AND name_norm LIKE @Name ESCAPE '\\'";
        }

        await using var connection = Open();
        return await QueryFoldersAsync(connection, sql + ";", parameters, cancellationToken);
    }

    public async Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        var rows = await connection.QueryAsync<TagCountRow>(new CommandDefinition(
            """
            SELECT MIN(tag) AS Name, COUNT(DISTINCT folder_id) AS Count
            FROM folder_tags GROUP BY tag_norm ORDER BY Count DESC, Name ASC;
            """, cancellationToken: cancellationToken));
        return rows.Select(r => new TagCount(r.Name, r.Count)).ToList();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        connection.Execute("PRAGMA journal_mode=WAL;");
        connection.Execute(SchemaSql);
    }

    private static async Task<IReadOnlyList<TaggedFolder>> QueryFoldersAsync(
        SqliteConnection connection, string folderSql, object? parameters, CancellationToken cancellationToken)
    {
        var folderRows = (await connection.QueryAsync<FolderRow>(
            new CommandDefinition(folderSql, parameters, cancellationToken: cancellationToken))).ToList();
        if (folderRows.Count == 0) return [];

        var ids = folderRows.Select(f => f.Id).ToList();
        var tagRows = await connection.QueryAsync<TagRow>(new CommandDefinition(
            "SELECT folder_id AS FolderId, tag AS Tag FROM folder_tags WHERE folder_id IN @Ids;",
            new { Ids = ids }, cancellationToken: cancellationToken));

        var tagsByFolder = tagRows
            .GroupBy(t => t.FolderId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.Tag).ToList(),
                StringComparer.Ordinal);

        return folderRows.Select(f => new TaggedFolder
        {
            Id = Guid.Parse(f.Id),
            RootPath = f.RootPath,
            RelativePath = f.RelativePath,
            Name = f.Name,
            Tags = tagsByFolder.TryGetValue(f.Id, out var tags) ? tags : [],
            UpdatedUtc = DateTimeOffset.Parse(f.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        }).ToList();
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public void Dispose() => SqliteConnection.ClearAllPools();

    private const string FolderColumns =
        "id AS Id, root_path AS RootPath, relative_path AS RelativePath, name AS Name, updated_utc AS UpdatedUtc";

    private const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS folders (
            id            TEXT PRIMARY KEY,
            root_path     TEXT NOT NULL,
            relative_path TEXT NOT NULL,
            name          TEXT NOT NULL,
            name_norm     TEXT NOT NULL,
            updated_utc   TEXT NOT NULL,
            UNIQUE (root_path, relative_path)
        );
        CREATE TABLE IF NOT EXISTS folder_tags (
            folder_id TEXT NOT NULL,
            tag       TEXT NOT NULL,
            tag_norm  TEXT NOT NULL,
            PRIMARY KEY (folder_id, tag_norm),
            FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_folder_tags_norm ON folder_tags(tag_norm);
        CREATE INDEX IF NOT EXISTS ix_folder_tags_folder ON folder_tags(folder_id);
        CREATE INDEX IF NOT EXISTS ix_folders_root ON folders(root_path);
        """;

    private sealed class FolderRow
    {
        public string Id { get; set; } = "";
        public string RootPath { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public string Name { get; set; } = "";
        public string UpdatedUtc { get; set; } = "";
    }

    private sealed class TagRow
    {
        public string FolderId { get; set; } = "";
        public string Tag { get; set; } = "";
    }

    private sealed class TagCountRow
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
}
