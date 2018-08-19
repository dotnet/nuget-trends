using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Protocol.Catalog;

namespace NuGetTrends.Api.Importing
{
    public class CatalogCursorStore : ICursor
    {
        internal const string CursorId = "Catalog";
        private readonly IServiceProvider _provider;

        public CatalogCursorStore(IServiceProvider provider) => _provider = provider;

        public async Task<DateTimeOffset?> GetAsync()
        {
            using (var context = _provider.GetRequiredService<NuGetTrendsContext>())
            {
               return (await context.Cursors.FindAsync(CursorId))?.Value;
            }
        }

        public async Task SetAsync(DateTimeOffset value)
        {
            using (var context = _provider.GetRequiredService<NuGetTrendsContext>())
            {
                var cursor = new Cursor
                {
                    Id = CursorId,
                    Value = value
                };
                context.Attach(cursor);
                await context.SaveChangesAsync();
            }
        }
    }
}
