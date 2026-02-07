namespace NuGetTrends.Data;

/// <summary>
/// Seeds sample data into PostgreSQL for local development.
/// Called after EF Core migrations in the Scheduler startup.
/// </summary>
public static class DevelopmentDataSeeder
{
    public static void SeedIfEmpty(NuGetTrendsContext db)
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

        var downloads = new (int Day, long Count)[]
        {
            (25, 48_000_000), (26, 48_100_000), (27, 48_200_000), (28, 48_350_000),
            (29, 48_500_000), (30, 48_620_000), (31, 48_750_000),
            (1,  48_830_000), (2,  48_900_000), (3,  49_050_000), (4,  49_200_000),
            (5,  49_350_000), (6,  49_480_000), (7,  49_600_000),
        };

        foreach (var (day, count) in downloads)
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
}
