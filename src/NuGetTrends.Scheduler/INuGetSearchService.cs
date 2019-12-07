using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Core.Types;

namespace NuGetTrends.Scheduler
{
    public interface INuGetSearchService
    {
        Task<IPackageSearchMetadata?> GetPackage(string packageId, CancellationToken token);
    }
}
