using System.Runtime.CompilerServices;
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
    /// Creates the migration tracking table in ClickHouse if it doesn't exist.
    /// </summary>
    private async Task EnsureMigrationTableExistsAsync(
        ClickHouseConnection connection,
        CancellationToken ct)
    {
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
    private async Task ApplyMigrationAsync(
        ClickHouseConnection connection,
        (string Name, string Content) migration,
        CancellationToken ct)
    {
        // Split on semicolons to handle multiple statements per file
        var statements = migration.Content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var statement in statements)
        {
            // Skip empty statements or comment-only statements
            var trimmed = statement.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Check if it's a comment-only block (all lines start with --)
            var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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
            cmd.CommandText = trimmed;
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
    /// Gets all migration scripts from the migrations directory, ordered by filename.
    /// </summary>
    private List<(string Name, string Content)> GetMigrationScripts([CallerFilePath] string callerFilePath = "")
    {
        // Navigate from this file to the migrations folder:
        // From: src/NuGetTrends.Data/ClickHouse/ClickHouseMigrationRunner.cs
        // To:   src/NuGetTrends.Data/ClickHouse/migrations/
        var thisFileDir = Path.GetDirectoryName(callerFilePath)
            ?? throw new InvalidOperationException("Could not determine directory of ClickHouseMigrationRunner.cs");

        var migrationsDir = Path.GetFullPath(Path.Combine(thisFileDir, "migrations"));

        if (!Directory.Exists(migrationsDir))
        {
            throw new DirectoryNotFoundException(
                $"ClickHouse migrations directory not found at '{migrationsDir}'. " +
                "The directory structure may have changed - please update the path in ClickHouseMigrationRunner.cs");
        }

        var sqlFiles = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (sqlFiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"No .sql migration files found in '{migrationsDir}'. " +
                "At least one migration file is required.");
        }

        return sqlFiles.Select(f => (Name: Path.GetFileName(f), Content: File.ReadAllText(f))).ToList();
    }
}
