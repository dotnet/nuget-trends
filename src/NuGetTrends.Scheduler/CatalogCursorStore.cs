using NuGet.Protocol.Catalog;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler;

public class CatalogCursorStore(NuGetTrendsContext context) : ICursor
{
    private const string CursorId = "Catalog";
    private readonly object[] _id = { CursorId };

    public async Task<DateTimeOffset?> GetAsync(CancellationToken token)
        => (await context.Cursors.FindAsync(_id, token))?.Value;

    public async Task SetAsync(DateTimeOffset value, CancellationToken token)
    {
        var cursor = await context.Cursors.FindAsync(_id, token);
        if (cursor is null)
        {
            throw new InvalidOperationException($"Expected to find a cursor named '{CursorId}'.");
        }
        cursor.Value = value;
        await context.SaveChangesAsync(token);

    }
}
