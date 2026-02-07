namespace NuGetTrends.Web.Client.Services;

/// <summary>
/// Tracks active HTTP request count and exposes loading state for the UI.
/// Replaces the JS-based fetch monkey-patching approach.
/// </summary>
public class LoadingState
{
    private int _activeRequests;

    /// <summary>
    /// Whether any requests are currently in flight.
    /// </summary>
    public bool IsLoading => _activeRequests > 0;

    /// <summary>
    /// Event raised when the loading state changes.
    /// </summary>
    public event Action? OnChange;

    public void Increment()
    {
        Interlocked.Increment(ref _activeRequests);
        OnChange?.Invoke();
    }

    public void Decrement()
    {
        var newValue = Interlocked.Decrement(ref _activeRequests);
        if (newValue < 0)
        {
            Interlocked.Exchange(ref _activeRequests, 0);
        }
        OnChange?.Invoke();
    }
}

/// <summary>
/// DelegatingHandler that intercepts HttpClient calls matching /api/ and
/// increments/decrements LoadingState around each request.
/// </summary>
public class LoadingStateHandler : DelegatingHandler
{
    private readonly LoadingState _loadingState;

    public LoadingStateHandler(LoadingState loadingState)
    {
        _loadingState = loadingState;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isApiCall = request.RequestUri?.PathAndQuery.Contains("/api/") == true;

        if (!isApiCall)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        _loadingState.Increment();
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            _loadingState.Decrement();
        }
    }
}
