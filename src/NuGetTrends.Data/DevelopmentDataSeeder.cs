using NuGetTrends.Data.ClickHouse;

namespace NuGetTrends.Data;

/// <summary>
/// Seeds sample data into PostgreSQL and ClickHouse for local development.
/// Called after EF Core migrations in the Scheduler startup.
/// </summary>
public static class DevelopmentDataSeeder
{
    private static readonly (int Day, long Count)[] Downloads =
    [
        (25, 48_000_000), (26, 48_100_000), (27, 48_200_000), (28, 48_350_000),
        (29, 48_500_000), (30, 48_620_000), (31, 48_750_000),
        (1,  48_830_000), (2,  48_900_000), (3,  49_050_000), (4,  49_200_000),
        (5,  49_350_000), (6,  49_480_000), (7,  49_600_000),
    ];

    public static void SeedPostgresIfEmpty(NuGetTrendsContext db)
    {
        if (db.PackageDownloads.Any())
        {
            return;
        }

        db.PackageDownloads.Add(new PackageDownload
        {
            PackageId = "Sentry",
            PackageIdLowered = "sentry",
            LatestDownloadCount = 49_600_000,
            LatestDownloadCountCheckedUtc = new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc),
            IconUrl = "https://raw.githubusercontent.com/getsentry/sentry-dotnet/main/assets/sentry-nuget.png"
        });

        foreach (var (day, count) in Downloads)
        {
            var month = day >= 25 ? 1 : 2;
            db.DailyDownloads.Add(new DailyDownload
            {
                PackageId = "Sentry",
                Date = new DateTime(2026, month, day, 0, 0, 0, DateTimeKind.Utc),
                DownloadCount = count
            });
        }

        db.SaveChanges();
    }

    public static async Task SeedClickHouseIfEmptyAsync(IClickHouseService clickHouseService)
    {
        // Check if data already exists
        var existing = await clickHouseService.GetWeeklyDownloadsAsync("sentry", months: 12);
        if (existing.Count > 0)
        {
            return;
        }

        var rows = Downloads.Select(d =>
        {
            var month = d.Day >= 25 ? 1 : 2;
            return (
                PackageId: "sentry",
                Date: new DateOnly(2026, month, d.Day),
                DownloadCount: d.Count
            );
        });

        await clickHouseService.InsertDailyDownloadsAsync(rows);
    }
}
