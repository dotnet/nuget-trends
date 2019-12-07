using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace NuGetTrends.Scheduler
{
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

            jobManager.AddOrUpdate<NuGetCatalogImporter>(
                "NuGetCatalogImporter",
                j => j.Import(JobCancellationToken.Null), // Hangfire passes in a token on activation
                Cron.Hourly());

            jobManager.AddOrUpdate<DailyDownloadPackageIdPublisher>(
                "DownloadCountImporter",
                j => j.Import(JobCancellationToken.Null),
                // Runs at 1 AM UTC
                Cron.Daily(1));
        }
    }
}
