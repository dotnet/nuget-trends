using Hangfire.States;

namespace NuGetTrends.Scheduler;

/// <summary>
/// Hangfire state election filter that intercepts job failures due to concurrent execution
/// and transitions them directly to DeletedState (skipping any retry attempts).
/// 
/// This is needed because we want [AutomaticRetry(Attempts = 1)] for genuine failures
/// (e.g., NuGet API errors after HTTP resilience is exhausted), but we want immediate
/// deletion (no retry) when a job is skipped due to another instance already running.
/// 
/// Without this filter, a job that throws ConcurrentExecutionSkippedException would
/// be retried once before being deleted, which is wasteful since the retry would also
/// fail if the original job is still running.
/// </summary>
public class SkipConcurrentExecutionFilter : IElectStateFilter
{
    public void OnStateElection(ElectStateContext context)
    {
        // When a job fails with ConcurrentExecutionSkippedException, skip retry and delete immediately
        if (context.CandidateState is FailedState failedState
            && failedState.Exception is ConcurrentExecutionSkippedException)
        {
            context.CandidateState = new DeletedState
            {
                Reason = failedState.Exception.Message
            };
        }
    }
}
