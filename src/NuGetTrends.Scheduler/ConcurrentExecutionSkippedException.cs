namespace NuGetTrends.Scheduler;

/// <summary>
/// Exception thrown when a job is skipped because another instance is already running.
/// This exception should not be reported to Sentry as an error - it's an expected condition
/// when multiple job triggers overlap.
/// </summary>
public class ConcurrentExecutionSkippedException : Exception
{
    public ConcurrentExecutionSkippedException(string message)
        : base(message)
    {
    }
}
