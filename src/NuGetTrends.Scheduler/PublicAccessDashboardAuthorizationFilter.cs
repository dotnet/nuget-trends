using Hangfire.Dashboard;

namespace NuGetTrends.Scheduler
{
    internal class PublicAccessDashboardAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context) => true;
    }
}
