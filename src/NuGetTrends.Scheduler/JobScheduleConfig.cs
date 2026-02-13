namespace NuGetTrends.Scheduler;

/// <summary>
/// Shared schedule configuration for Hangfire jobs and Sentry cron monitors.
/// These values are used by both RecurringJobManagerExtensions (Hangfire scheduling)
/// and the individual job classes (Sentry monitor configuration) to ensure they stay in sync.
/// </summary>
internal static class JobScheduleConfig
{
    /// <summary>
    /// NuGet Catalog Importer - runs hourly to sync new packages from NuGet.org catalog.
    /// </summary>
    internal static class CatalogImporter
    {
        public const string MonitorSlug = "nuget-catalog-importer";
        public const int IntervalHours = 1;
        public const int CheckInMarginMinutes = 5;
        public const int MaxRuntimeMinutes = 120; // 2 hours
        public const int FailureIssueThreshold = 2;
    }

    /// <summary>
    /// Daily Download Publisher - runs daily at 1 AM UTC to queue package IDs for download count fetching.
    /// </summary>
    internal static class DailyDownloadPublisher
    {
        public const string MonitorSlug = "daily-download-publisher";
        public const int RunAtHourUtc = 1;
        public const int CheckInMarginMinutes = 10;
        public const int MaxRuntimeMinutes = 60; // 1 hour
        public const int FailureIssueThreshold = 1; // Alert immediately - no retries configured
    }

    /// <summary>
    /// Trending Packages Snapshot Refresher - runs weekly on Monday at 2 AM UTC.
    /// </summary>
    internal static class TrendingSnapshotRefresher
    {
        public const string MonitorSlug = "trending-packages-snapshot-refresh";
        public const DayOfWeek RunOnDay = DayOfWeek.Monday;
        public const int RunAtHourUtc = 2;
        public const int CheckInMarginMinutes = 10;
        public const int MaxRuntimeMinutes = 30;
        public const int FailureIssueThreshold = 2; // Allow 1 retry before alerting
    }

    /// <summary>
    /// TFM Adoption Snapshot Refresher - runs weekly on Tuesday at 3 AM UTC.
    /// Computes cumulative package counts per TFM per month for the /frameworks page.
    /// </summary>
    internal static class TfmAdoptionRefresher
    {
        public const string MonitorSlug = "tfm-adoption-snapshot-refresh";
        public const DayOfWeek RunOnDay = DayOfWeek.Tuesday;
        public const int RunAtHourUtc = 3;
        public const int CheckInMarginMinutes = 15;
        public const int MaxRuntimeMinutes = 120;
        public const int FailureIssueThreshold = 2;
    }
}
