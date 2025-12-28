using System.Globalization;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Logging;

namespace NuGetTrends.Data.ClickHouse;

public class ClickHouseService : IClickHouseService
{
    private readonly string _connectionString;
    private readonly ILogger<ClickHouseService> _logger;

    public ClickHouseService(string connectionString, ILogger<ClickHouseService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task InsertDailyDownloadsAsync(
        IEnumerable<(string PackageId, DateOnly Date, long DownloadCount)> downloads,
        CancellationToken ct = default)
    {
        var downloadList = downloads.ToList();
        if (downloadList.Count == 0)
        {
            return;
        }

        await using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(ct);

        using var bulkCopy = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = "daily_downloads",
            ColumnNames = ["package_id", "date", "download_count"],
            BatchSize = downloadList.Count
        };

        var data = downloadList.Select(d => new object[]
        {
            d.PackageId.ToLower(CultureInfo.InvariantCulture),
            d.Date,
            (ulong)d.DownloadCount
        });

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(data, ct);

        _logger.LogDebug("Inserted {Count} daily downloads to ClickHouse", downloadList.Count);
    }

    public async Task<List<DailyDownloadResult>> GetWeeklyDownloadsAsync(
        string packageId,
        int months,
        CancellationToken ct = default)
    {
        await using var connection = new ClickHouseConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                toMonday(date) AS week,
                avg(download_count) AS download_count
            FROM daily_downloads
            WHERE package_id = {packageId:String}
              AND date >= today() - INTERVAL {months:Int32} MONTH
            GROUP BY week
            ORDER BY week
            """;

        var packageIdParam = cmd.CreateParameter();
        packageIdParam.ParameterName = "packageId";
        packageIdParam.Value = packageId.ToLower(CultureInfo.InvariantCulture);
        cmd.Parameters.Add(packageIdParam);

        var monthsParam = cmd.CreateParameter();
        monthsParam.ParameterName = "months";
        monthsParam.Value = months;
        cmd.Parameters.Add(monthsParam);

        var results = new List<DailyDownloadResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            results.Add(new DailyDownloadResult
            {
                Week = reader.GetDateTime(0),
                Count = reader.IsDBNull(1) ? null : (long?)reader.GetDouble(1)
            });
        }

        _logger.LogDebug("Retrieved {Count} weekly download results for package {PackageId}", results.Count, packageId);
        return results;
    }
}
