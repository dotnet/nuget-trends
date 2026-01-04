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
        jobManager.AddOrUpdate<NuGetCatalogImporter>(
            "NuGetCatalogImporter",
            j => j.Import(JobCancellationToken.Null, null),
            Cron.Hourly());

        jobManager.AddOrUpdate<DailyDownloadPackageIdPublisher>(
            "DownloadCountImporter",
            j => j.Import(JobCancellationToken.Null, null),
            // Runs at 1 AM UTC
            Cron.Daily(1));

        // Refresh the pre-computed trending packages snapshot weekly
        // Runs on Monday at 2 AM UTC (after daily download import completes)
        jobManager.AddOrUpdate<TrendingPackagesSnapshotRefresher>(
            "TrendingPackagesSnapshotRefresher",
            j => j.Refresh(JobCancellationToken.Null, null),
            Cron.Weekly(DayOfWeek.Monday, 2));
    }
}
