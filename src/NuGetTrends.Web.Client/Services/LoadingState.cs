namespace NuGetTrends.Web.Client.Services;

/// <summary>
/// Tracks active HTTP request count and exposes loading state for the UI.
/// Only shows the loading indicator after a short delay so fast responses
/// don't cause a visible flicker.
/// </summary>
public class LoadingState
{
    private int _activeRequests;
    private CancellationTokenSource? _delayCts;
    private bool _isVisible;

    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Whether the loading indicator should be visible.
    /// </summary>
    public bool IsLoading => _isVisible;

    /// <summary>
    /// Event raised when the loading state changes.
    /// </summary>
    public event Action? OnChange;

    public void Increment()
    {
        Interlocked.Increment(ref _activeRequests);
        _ = ShowAfterDelayAsync();
    }

    public void Decrement()
    {
        var newValue = Interlocked.Decrement(ref _activeRequests);
        if (newValue < 0)
        {
            Interlocked.Exchange(ref _activeRequests, 0);
        }

        if (_activeRequests <= 0)
        {
            _delayCts?.Cancel();
            _delayCts = null;

            if (_isVisible)
            {
                _isVisible = false;
                OnChange?.Invoke();
            }
        }
    }

    private async Task ShowAfterDelayAsync()
    {
        if (_isVisible)
        {
            return;
        }

        _delayCts?.Cancel();
        _delayCts?.Dispose();
        _delayCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(ShowDelay, _delayCts.Token);

            if (_activeRequests > 0 && !_isVisible)
            {
                _isVisible = true;
                OnChange?.Invoke();
            }
        }
        catch (TaskCanceledException)
        {
            // Request finished before delay elapsed — no flicker
        }
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
