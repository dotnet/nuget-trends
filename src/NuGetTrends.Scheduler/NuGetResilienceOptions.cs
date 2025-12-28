namespace NuGetTrends.Scheduler;

public class NuGetResilienceOptions
{
    public const string SectionName = "NuGetResilience";

    /// <summary>
    /// Default timeout for HTTP requests in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Timeout multiplier for retry attempts after a timeout.
    /// The retry timeout will be TimeoutSeconds * TimeoutRetryMultiplier.
    /// </summary>
    public double TimeoutRetryMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Base delay between retries in seconds.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 2;
}
