using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using NuGetTrends.Data;

namespace NuGetTrends.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class PackageController : ControllerBase
    {
        private readonly NuGetTrendsContext _context;

        public PackageController(NuGetTrendsContext context) => _context = context;

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<object>>> Search([FromQuery] string q, CancellationToken cancellationToken)
            => await _context.PackageDownloads
                .AsNoTracking()
                .Where(p => p.LatestDownloadCount != null
                            && p.PackageIdLowered.Contains(q.ToLower(CultureInfo.InvariantCulture)))
                .OrderByDescending(p => p.LatestDownloadCount)
                .Take(20)
                .Select(p => new
                {
                    p.PackageId,
                    p.LatestDownloadCount,
                    IconUrl = p.IconUrl ?? "https://www.nuget.org/Content/gallery/img/default-package-icon.svg"
                })
                .ToListAsync(cancellationToken);

        [HttpGet("history/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDownloadHistory(
            [FromRoute] string id,
            CancellationToken cancellationToken,
            [FromQuery] int months = 3)
        {
            if (! await _context.PackageDownloads.
                AnyAsync(p => p.PackageIdLowered == id.ToLower(CultureInfo.InvariantCulture), cancellationToken))
            {
                return NotFound();
            }

            var downloads = await _context.GetDailyDownloads(id, months);
            var data = new
            {
                Id = id,
                Downloads = downloads
            };

            return Ok(data);
        }


        [HttpGet("dependant/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IAsyncEnumerable<string> GetDependantPackageIds([FromRoute] string id)
            => (from r
                        in _context.ReversePackageDependencies.AsNoTracking()
                    where r.DependencyPackageIdLowered == id.ToLower(CultureInfo.InvariantCulture)
                    group r by r.PackageId
                    into g
                    select g.Key)
                .AsAsyncEnumerable();

        [HttpGet("dependant/{id}/version/{version?}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IEnumerable<DependantPackage>> GetDependantPackages(
            [FromRoute] string id,
            CancellationToken cancellationToken,
            [FromRoute] string? version = null)
            => from r
                    // TODO: Add paging here. This will blow up for sure.
                    in await _context.ReversePackageDependencies.AsNoTracking().ToListAsync(cancellationToken)
                let nugetVersion = version is null ? null : new NuGetVersion(version)
                where r.DependencyPackageIdLowered == id.ToLower(CultureInfo.InvariantCulture)
                    && (version is null ||
                    VersionRange.TryParse(r.DependencyRange, true, out var range)
                    && range.Satisfies(nugetVersion))
                group r by r.PackageId into g
                select new DependantPackage
                {
                    PackageId = g.Key,
                    Versions = from s in g
                        group s by s.PackageVersion into versions
                        select new VersionGroup
                        {
                            Version = versions.Key,
                            // TODO: Don't serialize null properties
                            TargetFrameworks = versions.All(v => string.IsNullOrEmpty(v?.TargetFramework))
                                ? null
                                : (from v in versions
                                    where v.TargetFramework != string.Empty // No TF specified
                                    select v.TargetFramework).Distinct(),
                        }
                };

        public class DependantPackage
        {
            public string PackageId { get; set; } = null!;
            public IEnumerable<VersionGroup> Versions { get; set; } = Enumerable.Empty<VersionGroup>();
        }

        public class VersionGroup
        {
            public string Version { get; set; } = null!;
            public IEnumerable<string>? TargetFrameworks { get; set; }
        }

    }
}
