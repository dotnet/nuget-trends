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
        public static void AddOrUpdate<TJob>(
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

            // TODO: Web hook to react to changes
            jobManager.AddOrUpdate<NuGetCatalogImporter>(
                "EmployeeImporterJob",
                j => j.Import(),
                Cron.Daily(10, 30));
        }
    }
}
