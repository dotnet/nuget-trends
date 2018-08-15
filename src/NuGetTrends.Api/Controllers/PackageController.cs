using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace NuGetTrends.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PackageController : ControllerBase
    {
        private readonly NuGetMustHavesContext _context;

        public PackageController(NuGetMustHavesContext context) => _context = context;

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<object>>> Search([FromQuery] string q)
            => await _context.NPackages
                .AsNoTracking()
                .Where(p => p.PackageId.Contains(q))
                .OrderByDescending(p => p.DownloadCount)
                .Take(100)
                .Select(p => new
                {
                    p.PackageId,
                    p.DownloadCount,
                    p.IconUrl
                })
                .ToListAsync();

        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetDownloadHistory([FromRoute] string id)
            => await _context.NPackages
                .AsNoTracking()
                .Where(p => p.PackageId == id)
                .Select(p => new
                {
                    p.PackageId,
                    p.DownloadCount
                })
                .FirstOrDefaultAsync();
    }
}
