using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Protocol.Catalog;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler
{
    public class CatalogCursorStore : ICursor
    {
        private const string CursorId = "Catalog";
        private readonly object[] _id = { CursorId };
        private readonly NuGetTrendsContext _context;

        public CatalogCursorStore(NuGetTrendsContext context) => _context = context;

        public async Task<DateTimeOffset?> GetAsync(CancellationToken token)
            => (await _context.Cursors.FindAsync(_id, token))?.Value;

        public async Task SetAsync(DateTimeOffset value, CancellationToken token)
        {
            var cursor = await _context.Cursors.FindAsync(_id, token);
            if (cursor is null)
            {
                throw new InvalidOperationException($"Expected to find a cursor named '{CursorId}'");
            }
            cursor.Value = value;
            _context.Update(cursor);
            await _context.SaveChangesAsync(token);

        }
    }
}
