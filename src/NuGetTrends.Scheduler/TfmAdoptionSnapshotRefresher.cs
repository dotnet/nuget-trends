using System.Data;
using ClickHouse.Driver.ADO;
using Hangfire;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Sentry;

namespace NuGetTrends.Scheduler;

/// <summary>
/// Hangfire job that refreshes the pre-computed TFM adoption snapshot in ClickHouse.
/// Queries PostgreSQL for package dependency groups, normalizes TFMs, computes cumulative
/// counts per month, and batch-inserts into ClickHouse for fast retrieval by the web app.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)] // 1 hour max
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [120, 600])]
public class TfmAdoptionSnapshotRefresher(
    IClickHouseService clickHouseService,
    NuGetTrendsContext dbContext,
    IConfiguration configuration,
    IHub hub,
    ILogger<TfmAdoptionSnapshotRefresher> logger)
{
    /// <summary>
    /// Number of months to recompute for incremental updates (self-healing window).
    /// </summary>
    private const int IncrementalRecomputeMonths = 3;

    public async Task Refresh(IJobCancellationToken token, PerformContext? context)
    {
        var jobId = context?.BackgroundJob?.Id ?? "unknown";

        // Start a new, independent transaction
        using var _ = hub.PushScope();
        var transactionContext = new TransactionContext(
            name: "tfm-adoption-snapshot-refresh",
            operation: "job",
            traceId: SentryId.Create(),
            spanId: SpanId.Create(),
            parentSpanId: null,
            isSampled: true);
        var transaction = hub.StartTransaction(transactionContext);
        hub.ConfigureScope(s =>
        {
            s.Transaction = transaction;
            s.SetTag("jobId", jobId);
        });

        var checkInId = hub.CaptureCheckIn(
            JobScheduleConfig.TfmAdoptionRefresher.MonitorSlug,
            CheckInStatus.InProgress,
            configureMonitorOptions: options =>
            {
                options.Interval(1, SentryMonitorInterval.Week);
                options.CheckInMargin = TimeSpan.FromMinutes(JobScheduleConfig.TfmAdoptionRefresher.CheckInMarginMinutes);
                options.MaxRuntime = TimeSpan.FromMinutes(JobScheduleConfig.TfmAdoptionRefresher.MaxRuntimeMinutes);
                options.TimeZone = "Etc/UTC";
                options.FailureIssueThreshold = JobScheduleConfig.TfmAdoptionRefresher.FailureIssueThreshold;
            });

        try
        {
            logger.LogInformation("Job {JobId}: Starting TFM adoption snapshot refresh", jobId);

            // Step 1: Determine mode (initial backfill vs incremental)
            var modeSpan = transaction.StartChild("clickhouse.check_mode", "Check if initial backfill or incremental");
            DateOnly? maxMonth = null;
            try
            {
                maxMonth = await GetMaxMonthAsync(token.ShutdownToken);
                modeSpan.SetData("mode", maxMonth.HasValue ? "incremental" : "backfill");
                modeSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                modeSpan.Finish(ex);
                throw;
            }

            var isBackfill = !maxMonth.HasValue;
            logger.LogInformation("Job {JobId}: Mode={Mode}, MaxMonth={MaxMonth}",
                jobId, isBackfill ? "backfill" : "incremental", maxMonth);

            // Step 2: Query PostgreSQL for package TFM data
            var querySpan = transaction.StartChild("postgres.query_tfm_data", "Query package TFM data from PostgreSQL");
            Dictionary<(string Tfm, string Family, DateOnly Month), HashSet<string>> tfmPackages;
            try
            {
                tfmPackages = await QueryPostgresAsync(maxMonth, isBackfill, token.ShutdownToken);
                querySpan.SetData("distinct_tfm_month_combos", tfmPackages.Count);
                querySpan.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                querySpan.Finish(ex);
                throw;
            }

            logger.LogInformation("Job {JobId}: Queried {Count} distinct (tfm, month) combos from PostgreSQL",
                jobId, tfmPackages.Count);

            if (tfmPackages.Count == 0)
            {
                logger.LogWarning("Job {JobId}: No TFM data found in PostgreSQL", jobId);
                transaction.Finish(SpanStatus.Ok);
                hub.CaptureCheckIn(JobScheduleConfig.TfmAdoptionRefresher.MonitorSlug, CheckInStatus.Ok, checkInId);
                return;
            }

            // Step 3: Compute cumulative sums
            var computeSpan = transaction.StartChild("compute.cumulative", "Compute cumulative package counts");
            List<TfmAdoptionDataPoint> dataPoints;
            try
            {
                dataPoints = ComputeCumulativeCounts(tfmPackages);
                computeSpan.SetData("data_points_count", dataPoints.Count);
                computeSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                computeSpan.Finish(ex);
                throw;
            }

            logger.LogInformation("Job {JobId}: Computed {Count} TFM adoption data points", jobId, dataPoints.Count);

            // Step 4: Insert into ClickHouse
            var insertSpan = transaction.StartChild("clickhouse.insert_snapshot", "Insert TFM adoption snapshot");
            int insertCount;
            try
            {
                insertCount = await clickHouseService.InsertTfmAdoptionSnapshotAsync(
                    dataPoints,
                    ct: token.ShutdownToken,
                    parentSpan: insertSpan);
                insertSpan.SetData("rows_inserted", insertCount);
                insertSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                insertSpan.Finish(ex);
                throw;
            }

            logger.LogInformation("Job {JobId}: TFM adoption snapshot refreshed with {Count} data points", jobId, insertCount);

            transaction.Finish(SpanStatus.Ok);
            hub.CaptureCheckIn(JobScheduleConfig.TfmAdoptionRefresher.MonitorSlug, CheckInStatus.Ok, checkInId);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Job {JobId}: TFM adoption snapshot refresh was cancelled", jobId);
            transaction.Finish(SpanStatus.Cancelled);
            hub.CaptureCheckIn(JobScheduleConfig.TfmAdoptionRefresher.MonitorSlug, CheckInStatus.Error, checkInId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId}: Failed to refresh TFM adoption snapshot", jobId);
            transaction.Finish(ex);
            hub.CaptureException(ex);
            hub.CaptureCheckIn(JobScheduleConfig.TfmAdoptionRefresher.MonitorSlug, CheckInStatus.Error, checkInId);
            throw;
        }
        finally
        {
            await hub.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }

    private async Task<DateOnly?> GetMaxMonthAsync(CancellationToken ct)
    {
        var connString = configuration.GetConnectionString("clickhouse")
            ?? configuration.GetConnectionString("ClickHouse")
            ?? throw new InvalidOperationException("ClickHouse connection string not configured.");
        connString = ClickHouseConnectionInfo.NormalizeConnectionString(connString);

        await using var connection = new ClickHouseConnection(connString);
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT max(month) FROM tfm_adoption_snapshot";
        var result = await cmd.ExecuteScalarAsync(ct);

        if (result is DateTime dt && dt > DateTime.MinValue)
        {
            return DateOnly.FromDateTime(dt);
        }

        return null;
    }

    private async Task<Dictionary<(string Tfm, string Family, DateOnly Month), HashSet<string>>> QueryPostgresAsync(
        DateOnly? maxMonth, bool isBackfill, CancellationToken ct)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var cmd = connection.CreateCommand();

        var sql = """
            SELECT pdcl.package_id_lowered, pdg.target_framework, MIN(pdcl.created) AS first_created
            FROM package_dependency_group pdg
            INNER JOIN package_details_catalog_leafs pdcl ON pdg.package_details_catalog_leaf_id = pdcl.id
            WHERE pdg.target_framework IS NOT NULL AND pdg.target_framework != ''
            """;

        if (!isBackfill && maxMonth.HasValue)
        {
            var recomputeStart = maxMonth.Value.AddMonths(-IncrementalRecomputeMonths);
            sql += " AND pdcl.created >= @recomputeStart";
            var param = cmd.CreateParameter();
            param.ParameterName = "recomputeStart";
            param.Value = recomputeStart.ToDateTime(TimeOnly.MinValue);
            param.DbType = DbType.DateTime;
            cmd.Parameters.Add(param);
        }

        sql += " GROUP BY pdcl.package_id_lowered, pdg.target_framework";
        cmd.CommandText = sql;

        var result = new Dictionary<(string Tfm, string Family, DateOnly Month), HashSet<string>>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rowCount = 0;
        while (await reader.ReadAsync(ct))
        {
            rowCount++;
            var packageId = reader.GetString(0);
            var rawTfm = reader.GetString(1);
            var firstCreated = reader.GetDateTime(2);

            var normalized = TfmNormalizer.Normalize(rawTfm);
            if (normalized is null)
            {
                continue;
            }

            var month = new DateOnly(firstCreated.Year, firstCreated.Month, 1);
            var key = (normalized.ShortName, normalized.Family, month);

            if (!result.TryGetValue(key, out var packages))
            {
                packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[key] = packages;
            }
            packages.Add(packageId);
        }

        logger.LogInformation("Processed {RowCount} rows from PostgreSQL into {ComboCount} (tfm, month) combos",
            rowCount, result.Count);

        return result;
    }

    private static List<TfmAdoptionDataPoint> ComputeCumulativeCounts(
        Dictionary<(string Tfm, string Family, DateOnly Month), HashSet<string>> tfmPackages)
    {
        // Group by (tfm, family), then sort by month and compute cumulative sums
        var grouped = tfmPackages
            .GroupBy(kv => (kv.Key.Tfm, kv.Key.Family))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(kv => kv.Key.Month)
                      .Select(kv => (kv.Key.Month, Count: (uint)kv.Value.Count))
                      .ToList());

        var result = new List<TfmAdoptionDataPoint>();

        foreach (var ((tfm, family), monthlyData) in grouped)
        {
            uint cumulative = 0;
            foreach (var (month, newCount) in monthlyData)
            {
                cumulative += newCount;
                result.Add(new TfmAdoptionDataPoint
                {
                    Month = month,
                    Tfm = tfm,
                    Family = family,
                    NewPackageCount = newCount,
                    CumulativePackageCount = cumulative
                });
            }
        }

        return result;
    }
}
