using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using ILogger = NuGet.Common.ILogger;

namespace NuGetTrends.Scheduler
{
    public class NuGetSearchService : INuGetSearchService
    {
        private readonly ILogger<NuGetSearchService> _logger;

        private static readonly ILogger NugetLogger = new NuGet.Common.NullLogger();
        private static readonly SearchFilter SearchFilter = new SearchFilter(true);

        private volatile PackageSearchResource? _packageSearchResource;

        private readonly SourceRepository _sourceRepository = new SourceRepository(
            new PackageSource("https://api.nuget.org/v3/index.json"),
            Repository.Provider.GetCoreV3());

        public NuGetSearchService(ILogger<NuGetSearchService> logger) => _logger = logger;

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
                    _logger.LogDebug("Package {packageId} not found.", packageId);
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
}
