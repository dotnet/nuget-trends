namespace NuGetTrends.Scheduler;

public class NuGetResilienceOptions
{
    public const string SectionName = "NuGetResilience";

    /// <summary>
    /// Default timeout for HTTP requests.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Multiplier applied to <see cref="Timeout"/> for retry attempts after a timeout.
    /// For example, with Timeout=10s and multiplier=2.0, retries will use a 20s timeout.
    /// </summary>
    public double TimeoutRetryMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Base delay between retries.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
