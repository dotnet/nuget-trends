using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using ILogger = NuGet.Common.ILogger;

namespace NuGetTrends.Scheduler;

public class NuGetSearchService(
    NuGetAvailabilityState availabilityState,
    ILogger<NuGetSearchService> logger) : INuGetSearchService
{
    private static readonly ILogger NugetLogger = new NuGet.Common.NullLogger();
    private static readonly SearchFilter SearchFilter = new(true);

    private volatile PackageSearchResource? _packageSearchResource;

    private readonly SourceRepository _sourceRepository = new(
        new PackageSource("https://api.nuget.org/v3/index.json"),
        Repository.Provider.GetCoreV3());

    /// <summary>
    /// Gets the package info
    /// </summary>
    /// <param name="packageId"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    /// <exception cref="NuGetUnavailableException">Thrown when NuGet API is marked unavailable.</exception>
    public async Task<IPackageSearchMetadata?> GetPackage(string packageId, CancellationToken token)
    {
        // Throw if NuGet is marked unavailable - caller should handle this differently from null (package not found)
        if (!availabilityState.IsAvailable)
        {
            logger.LogDebug(
                "Skipping NuGet API call for '{PackageId}' - NuGet marked unavailable since {UnavailableSince}",
                packageId, availabilityState.UnavailableSince);
            throw new NuGetUnavailableException(
                $"NuGet API unavailable since {availabilityState.UnavailableSince}. Skipping request for package '{packageId}'.");
        }

        if (_packageSearchResource == null)
        {
            // Yeah, it could get called it more than once
            _packageSearchResource = await _sourceRepository.GetResourceAsync<PackageSearchResource>(token);
        }

        try
        {
            // Search doesn't return matching id as the first result. MySqlConnector was the 7th, for example.
            var package = (await _packageSearchResource.SearchAsync($"packageid:{packageId}", SearchFilter, 0, 1, NugetLogger, token))
                .FirstOrDefault(p => p.Identity?.Id == packageId);

            if (package == null)
            {
                logger.LogDebug("Package with id '{packageId}' not found.", packageId);
            }

            // Success - mark NuGet as available
            availabilityState.MarkAvailable();

            return package;
        }
        catch (HttpRequestException e)
        {
            // HTTP failure - mark NuGet as unavailable
            e.AddSentryTag("packageId", packageId);
            availabilityState.MarkUnavailable(e);
            throw;
        }
        catch (Exception e)
        {
            e.AddSentryTag("packageId", packageId);
            throw;
        }
    }
}
