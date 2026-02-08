using System.Reflection;
using ClickHouse.Driver.ADO;
using Microsoft.Extensions.Logging;

namespace NuGetTrends.Data.ClickHouse;

/// <summary>
/// Manages ClickHouse schema migrations by tracking applied migrations and running new ones.
/// Similar to EF Core migrations but for ClickHouse SQL scripts.
/// </summary>
public class ClickHouseMigrationRunner
{
    private readonly string _connectionString;
    private readonly ILogger<ClickHouseMigrationRunner> _logger;

    public ClickHouseMigrationRunner(
        string connectionString,
        ILogger<ClickHouseMigrationRunner> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    /// <summary>
    /// Runs all pending migrations from the migrations directory.
    /// Creates the migration tracking table if it doesn't exist.
    /// </summary>
    public async Task RunMigrationsAsync(CancellationToken ct = default)
    {
        await using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Create the migration tracking table if it doesn't exist
        await EnsureMigrationTableExistsAsync(connection, ct);

        // Get list of migration scripts
        var migrationScripts = GetMigrationScripts();
        var appliedMigrations = await GetAppliedMigrationsAsync(connection, ct);

        var pendingMigrations = migrationScripts
            .Where(m => !appliedMigrations.Contains(m.Name))
            .ToList();

        if (pendingMigrations.Count == 0)
        {
            _logger.LogInformation("No pending ClickHouse migrations to apply");
            return;
        }

        _logger.LogInformation("Found {Count} pending ClickHouse migrations to apply", pendingMigrations.Count);

        foreach (var migration in pendingMigrations)
        {
            _logger.LogInformation("Applying ClickHouse migration: {MigrationName}", migration.Name);
            await ApplyMigrationAsync(connection, migration, ct);
            _logger.LogInformation("Successfully applied ClickHouse migration: {MigrationName}", migration.Name);
        }

        _logger.LogInformation("All ClickHouse migrations applied successfully");
    }

    /// <summary>
    /// Ensures the database and migration tracking table exist in ClickHouse.
    /// This must run before any migrations so the runner works on a completely fresh instance.
    /// </summary>
    private async Task EnsureMigrationTableExistsAsync(
        ClickHouseConnection connection,
        CancellationToken ct)
    {
        // The database must exist before we can create the tracking table.
        // On a fresh ClickHouse instance, it won't exist yet.
        await using (var dbCmd = connection.CreateCommand())
        {
            dbCmd.CommandText = "CREATE DATABASE IF NOT EXISTS nugettrends";
            await dbCmd.ExecuteNonQueryAsync(ct);
        }

        const string createTableSql = """
            CREATE TABLE IF NOT EXISTS nugettrends.clickhouse_migrations
            (
                migration_name String,
                applied_at DateTime DEFAULT now()
            )
            ENGINE = MergeTree()
            ORDER BY (migration_name)
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = createTableSql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Gets the list of already applied migrations from the tracking table.
    /// </summary>
    private async Task<HashSet<string>> GetAppliedMigrationsAsync(
        ClickHouseConnection connection,
        CancellationToken ct)
    {
        const string query = "SELECT migration_name FROM nugettrends.clickhouse_migrations";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = query;

        var appliedMigrations = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            appliedMigrations.Add(reader.GetString(0));
        }

        return appliedMigrations;
    }

    /// <summary>
    /// Applies a single migration by executing its SQL statements and recording it as applied.
    /// </summary>
    /// <remarks>
    /// Splits statements by semicolons. This is a simple approach that works for most ClickHouse
    /// migrations but does not handle semicolons within string literals or comments.
    /// If your migration contains semicolons in strings, use the ClickHouse string escape syntax
    /// or split the statement manually.
    /// </remarks>
    private async Task ApplyMigrationAsync(
        ClickHouseConnection connection,
        (string Name, string Content) migration,
        CancellationToken ct)
    {
        // Split on semicolons to handle multiple statements per file
        var statements = migration.Content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var statement in statements)
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            // Skip comment-only blocks (all lines start with --)
            var lines = statement.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var hasNonCommentLine = lines.Any(line =>
            {
                var l = line.Trim();
                return !string.IsNullOrEmpty(l) && !l.StartsWith("--");
            });

            if (!hasNonCommentLine)
            {
                continue;
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Record the migration as applied
        await RecordMigrationAsync(connection, migration.Name, ct);
    }

    /// <summary>
    /// Records a migration as applied in the tracking table.
    /// </summary>
    private async Task RecordMigrationAsync(
        ClickHouseConnection connection,
        string migrationName,
        CancellationToken ct)
    {
        const string insertSql = "INSERT INTO nugettrends.clickhouse_migrations (migration_name) VALUES ({migrationName:String})";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = insertSql;

        var param = cmd.CreateParameter();
        param.ParameterName = "migrationName";
        param.Value = migrationName;
        cmd.Parameters.Add(param);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Gets all migration scripts from embedded resources, ordered by filename.
    /// Migration SQL files are embedded in the assembly at build time.
    /// </summary>
    private List<(string Name, string Content)> GetMigrationScripts()
    {
        var assembly = typeof(ClickHouseMigrationRunner).Assembly;
        var resourcePrefix = $"{assembly.GetName().Name}.ClickHouse.migrations.";
        
        // Get all embedded resource names that are SQL files in the migrations folder
        var migrationResources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix) && name.EndsWith(".sql"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (migrationResources.Count == 0)
        {
            throw new InvalidOperationException(
                "No embedded .sql migration files found. " +
                "Ensure migration SQL files are marked as EmbeddedResource in NuGetTrends.Data.csproj");
        }

        var migrations = new List<(string Name, string Content)>();
        foreach (var resourceName in migrationResources)
        {
            // Extract filename from resource name (e.g., "NuGetTrends.Data.ClickHouse.migrations.2025-12-26-01-init.sql" -> "2025-12-26-01-init.sql")
            var fileName = resourceName.Substring(resourcePrefix.Length);
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Failed to read embedded resource: {resourceName}");
            }

            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            migrations.Add((fileName, content));
        }

        return migrations;
    }
}
