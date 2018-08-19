using System;
using System.Threading.Tasks;
using NuGet.Protocol.Catalog;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler
{
    public class CatalogCursorStore : ICursor
    {
        internal const string CursorId = "Catalog";
        private readonly NuGetTrendsContext _context;

        public CatalogCursorStore(NuGetTrendsContext context) => _context = context;

        public async Task<DateTimeOffset?> GetAsync()
        {
            return (await _context.Cursors.FindAsync(CursorId))?.Value;
        }

        public async Task SetAsync(DateTimeOffset value)
        {
            var cursor = new Cursor
            {
                Id = CursorId,
                Value = value
            };
            _context.Attach(cursor);
            await _context.SaveChangesAsync();
        }
    }
}
