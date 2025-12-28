namespace NuGetTrends.Scheduler;

public class NuGetResilienceOptions
{
    public const string SectionName = "NuGetResilience";

    private const double DefaultRetryTimeoutMultiplier = 2.0;

    private TimeSpan? _retryTimeout;

    /// <summary>
    /// Default timeout for HTTP requests.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for retry attempts. Defaults to <see cref="Timeout"/> × 2.
    /// </summary>
    public TimeSpan RetryTimeout
    {
        get => _retryTimeout ?? Timeout * DefaultRetryTimeoutMultiplier;
        set => _retryTimeout = value;
    }

    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Base delay between retries.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
