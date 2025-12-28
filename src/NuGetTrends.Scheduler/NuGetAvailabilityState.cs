namespace NuGetTrends.Scheduler;

/// <summary>
/// Thread-safe singleton that tracks NuGet API availability.
/// When NuGet becomes unavailable (detected via HTTP failures), subsequent job runs
/// will skip execution until the cooldown period expires.
/// </summary>
public class NuGetAvailabilityState
{
    private long _unavailableSinceTicks = 0; // 0 means available

    /// <summary>
    /// How long to wait before allowing retry after NuGet becomes unavailable.
    /// </summary>
    public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets whether NuGet API is currently available.
    /// Auto-resets to available after <see cref="CooldownPeriod"/> expires.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            var ticks = Interlocked.Read(ref _unavailableSinceTicks);
            if (ticks == 0)
            {
                return true;
            }

            // Auto-reset after cooldown to allow retry
            var unavailableSince = new DateTimeOffset(ticks, TimeSpan.Zero);
            if (DateTimeOffset.UtcNow - unavailableSince > CooldownPeriod)
            {
                // Try to reset - if someone else already did, that's fine
                Interlocked.CompareExchange(ref _unavailableSinceTicks, 0, ticks);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets when NuGet was marked as unavailable. Returns null if currently available.
    /// </summary>
    public DateTimeOffset? UnavailableSince
    {
        get
        {
            var ticks = Interlocked.Read(ref _unavailableSinceTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Marks NuGet API as unavailable. Only captures a Sentry event on the first transition
    /// to unavailable state (not on subsequent calls while already unavailable).
    /// </summary>
    /// <param name="exception">The exception that caused the unavailability.</param>
    public void MarkUnavailable(Exception? exception = null)
    {
        var newTicks = DateTimeOffset.UtcNow.UtcTicks;

        // Only transition if currently available (ticks == 0)
        var previousTicks = Interlocked.CompareExchange(ref _unavailableSinceTicks, newTicks, 0);

        // If we were the one to transition from available to unavailable, capture Sentry event
        if (previousTicks == 0)
        {
            SentrySdk.ConfigureScope(scope =>
            {
                scope.SetTag("nuget.availability", "unavailable");
                scope.SetExtra("cooldownMinutes", CooldownPeriod.TotalMinutes);
            });

            if (exception != null)
            {
                SentrySdk.CaptureException(exception, scope =>
                {
                    scope.SetTag("nuget.outage", "true");
                });
            }
            else
            {
                SentrySdk.CaptureMessage(
                    "NuGet API unavailable - skipping jobs until cooldown expires",
                    SentryLevel.Warning);
            }
        }
    }

    /// <summary>
    /// Marks NuGet API as available. Call this when a request succeeds after a previous failure.
    /// </summary>
    public void MarkAvailable()
    {
        Interlocked.Exchange(ref _unavailableSinceTicks, 0);
    }

    /// <summary>
    /// Resets the state to available. Useful for testing.
    /// </summary>
    internal void Reset()
    {
        Interlocked.Exchange(ref _unavailableSinceTicks, 0);
    }
}
