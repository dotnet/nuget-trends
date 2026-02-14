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

        db.PackageDownloads.Add(new PackageDownload
        {
            PackageId = "Newtonsoft.Json",
            PackageIdLowered = "newtonsoft.json",
            LatestDownloadCount = 99_200_000,
            LatestDownloadCountCheckedUtc = new DateTime(2026, 2, 7, 0, 0, 0, DateTimeKind.Utc),
            IconUrl = "https://www.newtonsoft.com/content/images/nugeticon.png"
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
            db.DailyDownloads.Add(new DailyDownload
            {
                PackageId = "Newtonsoft.Json",
                Date = new DateTime(2026, month, day, 0, 0, 0, DateTimeKind.Utc),
                DownloadCount = count * 2
            });
        }

        db.SaveChanges();
    }

    public static async Task SeedClickHouseIfEmptyAsync(IClickHouseService clickHouseService)
    {
        await SeedDailyDownloadsAsync(clickHouseService);
        await SeedTfmAdoptionDataAsync(clickHouseService);
    }

    private static async Task SeedDailyDownloadsAsync(IClickHouseService clickHouseService)
    {
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

        // Seed a second package (Newtonsoft.Json) for multi-package testing
        var newtonsoftRows = Downloads.Select(d =>
        {
            var month = d.Day >= 25 ? 1 : 2;
            return (
                PackageId: "newtonsoft.json",
                Date: new DateOnly(2026, month, d.Day),
                DownloadCount: d.Count * 2 // Different scale for visual distinction
            );
        });

        await clickHouseService.InsertDailyDownloadsAsync(newtonsoftRows);
    }

    private static async Task SeedTfmAdoptionDataAsync(IClickHouseService clickHouseService)
    {
        var existingTfms = await clickHouseService.GetTfmAdoptionFromSnapshotAsync();
        if (existingTfms.Count > 0)
        {
            return;
        }

        // Simulate realistic TFM adoption: each .NET version releases yearly in November,
        // grows quickly, then plateaus as the next version takes over.
        // Data spans Nov 2021 – Jan 2026 (~50 months).
        var start = new DateOnly(2021, 11, 1);
        var end = new DateOnly(2026, 1, 1);
        var months = new List<DateOnly>();
        for (var m = start; m <= end; m = m.AddMonths(1))
            months.Add(m);

        // (tfm, family, release month, peak monthly new packages, months to peak)
        var tfmLifecycles = new (string Tfm, string Family, DateOnly Release, uint PeakNew, int MonthsToPeak)[]
        {
            // .NET Framework 4.8 – mature, slow but steady, never really stops
            ("net48", ".NET Framework", new DateOnly(2019, 4, 1), 400u, 6),
            // .NET Standard 2.0 – was dominant, slowly declining as modern .NET takes over
            ("netstandard2.0", ".NET Standard", new DateOnly(2017, 8, 1), 600u, 6),
            // .NET 6 (LTS) – released Nov 2021, strong adoption, plateaus after .NET 8
            ("net6.0", ".NET", new DateOnly(2021, 11, 1), 3200u, 10),
            // .NET 7 – released Nov 2022, shorter lifecycle (STS), overtaken quickly by .NET 8
            ("net7.0", ".NET", new DateOnly(2022, 11, 1), 1800u, 8),
            // .NET 8 (LTS) – released Nov 2023, strong adoption, the new king
            ("net8.0", ".NET", new DateOnly(2023, 11, 1), 3500u, 10),
            // .NET 9 – released Nov 2024, just getting started
            ("net9.0", ".NET", new DateOnly(2024, 11, 1), 2000u, 8),
        };

        var dataPoints = new List<TfmAdoptionDataPoint>();

        foreach (var (tfm, family, release, peakNew, monthsToPeak) in tfmLifecycles)
        {
            uint cumulative = 0;

            // For frameworks released before our data window, give them a head start
            if (release < start)
            {
                var headStartMonths = (start.Year - release.Year) * 12 + (start.Month - release.Month);
                cumulative = (uint)(peakNew * Math.Min(headStartMonths, 30));
            }

            foreach (var month in months)
            {
                var monthsSinceRelease = (month.Year - release.Year) * 12 + (month.Month - release.Month);

                uint newPackages;
                if (monthsSinceRelease < 0)
                {
                    // Not released yet
                    continue;
                }
                else if (monthsSinceRelease <= monthsToPeak)
                {
                    // Ramp up: accelerating adoption toward peak
                    var progress = (double)monthsSinceRelease / monthsToPeak;
                    newPackages = (uint)(peakNew * progress);
                }
                else
                {
                    // Decay: adoption slows as next version takes over.
                    // Exponential decay toward a floor (~5% of peak), never reaches zero.
                    var monthsPastPeak = monthsSinceRelease - monthsToPeak;
                    var decayRate = 0.12; // faster decay = quicker handoff
                    var floor = peakNew * 0.05;
                    newPackages = (uint)(floor + (peakNew - floor) * Math.Exp(-decayRate * monthsPastPeak));
                }

                // Ensure at least 1 new package per month once released
                newPackages = Math.Max(newPackages, 1);
                cumulative += newPackages;

                dataPoints.Add(new TfmAdoptionDataPoint
                {
                    Month = month,
                    Tfm = tfm,
                    Family = family,
                    NewPackageCount = newPackages,
                    CumulativePackageCount = cumulative,
                });
            }
        }

        await clickHouseService.InsertTfmAdoptionSnapshotAsync(dataPoints);
    }
}
