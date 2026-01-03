using System.Runtime.CompilerServices;
using ClickHouse.Client.ADO;
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
    /// Connection string without the database, for admin operations.
    /// </summary>
    private string AdminConnectionString => $"Host={_container.Hostname};Port={_container.GetMappedPublicPort(8123)};Username={Username};Password={Password}";

    /// <summary>
    /// Resets all tables by truncating data.
    /// Call this at the start of each test for isolation.
    /// </summary>
    public async Task ResetTableAsync()
    {
        await using var connection = new ClickHouseConnection(ConnectionString);
        await connection.OpenAsync();

        await using var truncateDailyCmd = connection.CreateCommand();
        truncateDailyCmd.CommandText = "TRUNCATE TABLE daily_downloads";
        await truncateDailyCmd.ExecuteNonQueryAsync();

        await using var truncateWeeklyCmd = connection.CreateCommand();
        truncateWeeklyCmd.CommandText = "TRUNCATE TABLE weekly_downloads";
        await truncateWeeklyCmd.ExecuteNonQueryAsync();
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
}

/// <summary>
/// Collection definition for ClickHouse tests.
/// Tests in this collection share the same ClickHouseFixture instance.
/// </summary>
[CollectionDefinition("ClickHouse")]
public class ClickHouseCollection : ICollectionFixture<ClickHouseFixture>
{
}
