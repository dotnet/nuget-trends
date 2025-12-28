namespace NuGetTrends.Scheduler;

/// <summary>
/// Thread-safe singleton that tracks NuGet API availability.
/// When NuGet becomes unavailable (detected via HTTP failures), subsequent job runs
/// will skip execution until the cooldown period expires.
/// </summary>
public class NuGetAvailabilityState
{
    private volatile bool _isAvailable = true;
    private DateTimeOffset _unavailableSince = DateTimeOffset.MinValue;
    private readonly object _lock = new();

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
            if (_isAvailable)
            {
                return true;
            }

            // Auto-reset after cooldown to allow retry
            if (DateTimeOffset.UtcNow - _unavailableSince > CooldownPeriod)
            {
                lock (_lock)
                {
                    if (!_isAvailable && DateTimeOffset.UtcNow - _unavailableSince > CooldownPeriod)
                    {
                        _isAvailable = true;
                    }
                }
            }

            return _isAvailable;
        }
    }

    /// <summary>
    /// Gets when NuGet was marked as unavailable. Returns null if currently available.
    /// </summary>
    public DateTimeOffset? UnavailableSince => _isAvailable ? null : _unavailableSince;

    /// <summary>
    /// Marks NuGet API as unavailable. Only captures a Sentry event on the first transition
    /// to unavailable state (not on subsequent calls while already unavailable).
    /// </summary>
    /// <param name="exception">The exception that caused the unavailability.</param>
    public void MarkUnavailable(Exception? exception = null)
    {
        if (_isAvailable)
        {
            lock (_lock)
            {
                if (_isAvailable)
                {
                    _isAvailable = false;
                    _unavailableSince = DateTimeOffset.UtcNow;

                    // Capture a single Sentry event for the outage
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
        }
    }

    /// <summary>
    /// Marks NuGet API as available. Call this when a request succeeds after a previous failure.
    /// </summary>
    public void MarkAvailable()
    {
        if (!_isAvailable)
        {
            lock (_lock)
            {
                _isAvailable = true;
            }
        }
    }

    /// <summary>
    /// Resets the state to available. Useful for testing.
    /// </summary>
    internal void Reset()
    {
        lock (_lock)
        {
            _isAvailable = true;
            _unavailableSince = DateTimeOffset.MinValue;
        }
    }
}
