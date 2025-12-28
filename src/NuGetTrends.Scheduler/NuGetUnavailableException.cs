namespace NuGetTrends.Scheduler;

/// <summary>
/// Exception thrown when NuGet API is unavailable and requests should be skipped.
/// </summary>
public class NuGetUnavailableException : Exception
{
    public NuGetUnavailableException()
        : base("NuGet API is unavailable.")
    {
    }

    public NuGetUnavailableException(string message)
        : base(message)
    {
    }

    public NuGetUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
