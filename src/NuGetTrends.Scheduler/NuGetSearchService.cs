using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using ILogger = NuGet.Common.ILogger;

namespace NuGetTrends.Scheduler;

public class NuGetSearchService(ILogger<NuGetSearchService> logger) : INuGetSearchService
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
    public async Task<IPackageSearchMetadata?> GetPackage(string packageId, CancellationToken token)
    {
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
                logger.LogDebug("Package {packageId} not found.", packageId);
            }

            return package;

        }
        catch (Exception e)
        {
            e.Data["PackageId"] = packageId;
            throw;
        }
    }
}