using System.Linq.Expressions;
using Hangfire;
using Hangfire.Common;

namespace NuGetTrends.Scheduler;

internal static class RecurringJobManagerExtensions
{
    private static void AddOrUpdate<TJob>(
        this IRecurringJobManager manager,
        string recurringJobId,
        Expression<Func<TJob, Task>> jobExpression,
        string cronExpression)
    {
        var job = Job.FromExpression(jobExpression);
        manager.AddOrUpdate(recurringJobId, job, cronExpression);
    }

    internal static void ScheduleJobs(this IApplicationBuilder app)
    {
        var jobManager = app.ApplicationServices.GetRequiredService<IRecurringJobManager>();

        // Hangfire injects IJobCancellationToken and PerformContext at runtime
        // Schedule values are defined in JobScheduleConfig to stay in sync with Sentry monitor config
        jobManager.AddOrUpdate<NuGetCatalogImporter>(
            "NuGetCatalogImporter",
            j => j.Import(JobCancellationToken.Null, null),
            Cron.HourInterval(JobScheduleConfig.CatalogImporter.IntervalHours));

        jobManager.AddOrUpdate<DailyDownloadPackageIdPublisher>(
            "DownloadCountImporter",
            j => j.Import(JobCancellationToken.Null, null),
            Cron.Daily(JobScheduleConfig.DailyDownloadPublisher.RunAtHourUtc));

        jobManager.AddOrUpdate<TrendingPackagesSnapshotRefresher>(
            "TrendingPackagesSnapshotRefresher",
            j => j.Refresh(JobCancellationToken.Null, null),
            Cron.Weekly(JobScheduleConfig.TrendingSnapshotRefresher.RunOnDay,
                JobScheduleConfig.TrendingSnapshotRefresher.RunAtHourUtc));

        jobManager.AddOrUpdate<TfmAdoptionSnapshotRefresher>(
            "TfmAdoptionSnapshotRefresher",
            j => j.Refresh(JobCancellationToken.Null, null),
            Cron.Weekly(JobScheduleConfig.TfmAdoptionRefresher.RunOnDay,
                JobScheduleConfig.TfmAdoptionRefresher.RunAtHourUtc));
    }
}
