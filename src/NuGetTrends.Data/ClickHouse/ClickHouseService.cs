using System.Globalization;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NuGetTrends.Data.ClickHouse;

public class ClickHouseService : IClickHouseService
{
    private readonly ClickHouseOptions _options;
    private readonly ILogger<ClickHouseService> _logger;

    public ClickHouseService(IOptions<ClickHouseOptions> options, ILogger<ClickHouseService> logger)
    {
        _options = options.Value;
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

        await using var connection = new ClickHouseConnection(_options.ConnectionString);
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
        await using var connection = new ClickHouseConnection(_options.ConnectionString);
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

    public async Task<HashSet<string>> GetPackagesWithDownloadsForDateAsync(
        DateOnly date,
        CancellationToken ct = default)
    {
        await using var connection = new ClickHouseConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT package_id
            FROM daily_downloads
            WHERE date = {date:Date}
            """;

        var dateParam = cmd.CreateParameter();
        dateParam.ParameterName = "date";
        dateParam.Value = date;
        cmd.Parameters.Add(dateParam);

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }

        _logger.LogDebug("Found {Count} packages with downloads for date {Date}", results.Count, date);
        return results;
    }
}
