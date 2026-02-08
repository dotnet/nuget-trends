using System.Runtime.CompilerServices;
using ClickHouse.Driver.ADO;
using Testcontainers.ClickHouse;
using Xunit;

namespace NuGetTrends.Data.Tests.Infrastructure;

/// <summary>
/// xUnit fixture that manages a ClickHouse container for integration tests.
/// The container is started once per test class and disposed after all tests complete.
/// </summary>
public class ClickHouseFixture : IAsyncLifetime
{
    // Keep in sync with docker-compose.yml (currently clickhouse/clickhouse-server:25.11.5)
    private const string ClickHouseImage = "clickhouse/clickhouse-server:25.11.5";
    private const string DatabaseName = "nugettrends";
    private const string Username = "clickhouse";
    private const string Password = "clickhouse";

    private readonly ClickHouseContainer _container;

    public ClickHouseFixture()
    {
        _container = new ClickHouseBuilder()
            .WithImage(ClickHouseImage)
            .WithUsername(Username)
            .WithPassword(Password)
            .Build();
    }

    /// <summary>
    /// Connection string for tests. Includes the database name.
    /// </summary>
    public string ConnectionString => $"Host={_container.Hostname};Port={_container.GetMappedPublicPort(8123)};Database={DatabaseName};Username={Username};Password={Password}";

    /// <summary>
    /// Connection string without the database, for admin operations.
    /// </summary>
    public string AdminConnectionString => $"Host={_container.Hostname};Port={_container.GetMappedPublicPort(8123)};Username={Username};Password={Password}";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await RunMigrationsAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Resets all tables by dropping and recreating them.
    /// Call this at the start of each test for isolation.
    /// </summary>
    /// <remarks>
    /// We use DROP + CREATE instead of TRUNCATE because:
    /// 1. The weekly_downloads table uses AggregatingMergeTree with a Materialized View
    /// 2. TRUNCATE on AggregatingMergeTree doesn't properly reset aggregate state
    /// 3. The MV continues to accumulate data even after truncate
    /// Dropping and recreating ensures a completely clean slate for each test.
    /// </remarks>
    public async Task ResetTableAsync()
    {
        await using var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();

        // Drop in reverse dependency order: MV first, then tables
        var dropStatements = new[]
        {
            "DROP VIEW IF EXISTS weekly_downloads_mv",
            "DROP TABLE IF EXISTS weekly_downloads",
            "DROP TABLE IF EXISTS trending_packages_snapshot",
            "DROP TABLE IF EXISTS package_first_seen",
            "DROP TABLE IF EXISTS daily_downloads"
        };

        foreach (var sql in dropStatements)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        // Recreate tables by running migrations
        await using var adminConnection = new ClickHouseConnection(AdminConnectionString);
        await adminConnection.OpenAsync();

        var migrationScripts = GetMigrationScripts();
        foreach (var script in migrationScripts)
        {
            var statements = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var statement in statements)
            {
                var trimmed = statement.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

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

                await using var cmd = adminConnection.CreateCommand();
                cmd.CommandText = trimmed;
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    /// <summary>
    /// Executes a raw SQL command against the database.
    /// Useful for verifying test data.
    /// </summary>
    public async Task<T> ExecuteScalarAsync<T>(string sql)
    {
        await using var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    /// <summary>
    /// Forces ReplacingMergeTree to deduplicate rows immediately.
    /// Normally deduplication happens during background merges, but this forces it for testing.
    /// </summary>
    public async Task OptimizeTableAsync()
    {
        await using var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "OPTIMIZE TABLE daily_downloads FINAL";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Populates package_first_seen with all packages from weekly_downloads.
    /// This is needed for tests that use GetTrendingPackagesAsync, which joins against package_first_seen.
    /// Call this after inserting test data into daily_downloads.
    /// </summary>
    public async Task PopulatePackageFirstSeenAsync()
    {
        await using var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();

        // Insert all packages from weekly_downloads that aren't already tracked
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO package_first_seen (package_id, first_seen)
            SELECT package_id, min(week) AS first_seen
            FROM weekly_downloads
            WHERE package_id NOT IN (SELECT package_id FROM package_first_seen)
            GROUP BY package_id
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Executes a raw SQL command and returns all rows as a list.
    /// </summary>
    public async Task<List<T>> ExecuteQueryAsync<T>(string sql, Func<System.Data.Common.DbDataReader, T> mapper)
    {
        await using var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();

        var results = new List<T>();
        while (await reader.ReadAsync())
        {
            results.Add(mapper(reader));
        }
        return results;
    }

    private async Task RunMigrationsAsync()
    {
        var migrationScripts = GetMigrationScripts();

        await using var connection = new ClickHouseConnection(AdminConnectionString);
        await connection.OpenAsync();

        foreach (var script in migrationScripts)
        {
            // Split on semicolons to handle multiple statements per file
            var statements = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private static List<string> GetMigrationScripts([CallerFilePath] string callerFilePath = "")
    {
        // Navigate from this file to the migrations folder:
        // From: src/NuGetTrends.Data.Tests/Infrastructure/ClickHouseFixture.cs
        // To:   src/NuGetTrends.Data/ClickHouse/migrations/
        var thisFileDir = Path.GetDirectoryName(callerFilePath)
            ?? throw new InvalidOperationException("Could not determine directory of ClickHouseFixture.cs");

        var migrationsDir = Path.GetFullPath(
            Path.Combine(thisFileDir, "..", "..", "NuGetTrends.Data", "ClickHouse", "migrations"));

        if (!Directory.Exists(migrationsDir))
        {
            throw new DirectoryNotFoundException(
                $"ClickHouse migrations directory not found at '{migrationsDir}'. " +
                "The directory structure may have changed - please update the path in ClickHouseFixture.cs");
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

        return sqlFiles.Select(File.ReadAllText).ToList();
    }

    /// <summary>
    /// Checks if a table exists in ClickHouse.
    /// </summary>
    public async Task<bool> TableExistsAsync(string tableName)
    {
        await using var connection = new ClickHouseConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count() FROM system.tables WHERE database = {database:String} AND name = {table:String}";
        
        var dbParam = cmd.CreateParameter();
        dbParam.ParameterName = "database";
        dbParam.Value = tableName.Contains('.') ? tableName.Split('.')[0] : DatabaseName;
        cmd.Parameters.Add(dbParam);

        var tableParam = cmd.CreateParameter();
        tableParam.ParameterName = "table";
        tableParam.Value = tableName.Contains('.') ? tableName.Split('.')[1] : tableName;
        cmd.Parameters.Add(tableParam);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    /// <summary>
    /// Drops the entire nugettrends database, giving the migration runner a completely fresh start.
    /// </summary>
    public async Task DropDatabaseAsync()
    {
        await using var connection = new ClickHouseConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DROP DATABASE IF EXISTS nugettrends";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Executes a raw SQL command against the admin connection (no database context).
    /// </summary>
    public async Task ExecuteNonQueryAsync(string sql)
    {
        await using var connection = new ClickHouseConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Drops only the migration tracking table, simulating a database that was set up
    /// manually (or by a previous version) without migration tracking.
    /// </summary>
    public async Task DropMigrationTrackingAsync()
    {
        await using var connection = new ClickHouseConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DROP TABLE IF EXISTS nugettrends.clickhouse_migrations";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts a migration record into the tracking table without actually running the migration.
    /// Used to simulate partial migration state in tests.
    /// </summary>
    public async Task InsertMigrationRecordAsync(string migrationName)
    {
        await using var connection = new ClickHouseConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO nugettrends.clickhouse_migrations (migration_name) VALUES ({migrationName:String})";

        var param = cmd.CreateParameter();
        param.ParameterName = "migrationName";
        param.Value = migrationName;
        cmd.Parameters.Add(param);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Gets the list of applied migrations from the tracking table.
    /// </summary>
    public async Task<List<string>> GetAppliedMigrationsAsync()
    {
        await using var connection = new ClickHouseConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT migration_name FROM nugettrends.clickhouse_migrations ORDER BY migration_name";

        var migrations = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            migrations.Add(reader.GetString(0));
        }
        return migrations;
    }
}

/// <summary>
/// Collection definition for ClickHouse tests.
/// Tests in this collection share the same ClickHouseFixture instance.
/// </summary>
[CollectionDefinition("ClickHouse")]
public class ClickHouseCollection : ICollectionFixture<ClickHouseFixture>
{
}
