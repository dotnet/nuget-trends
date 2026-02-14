using System.Text.Json;
using Xunit;
using FluentAssertions;
using NuGetTrends.Web.Client;

// Alias to avoid conflicts with server-side DTOs in NuGetTrends.Web namespace
using PackageSearchResult = NuGetTrends.Web.Client.Models.PackageSearchResult;
using PackageDownloadHistory = NuGetTrends.Web.Client.Models.PackageDownloadHistory;
using DownloadStats = NuGetTrends.Web.Client.Models.DownloadStats;
using TrendingPackage = NuGetTrends.Web.Client.Models.TrendingPackage;
using TfmFamilyGroup = NuGetTrends.Web.Client.Models.TfmFamilyGroup;
using TfmAdoptionResponse = NuGetTrends.Web.Client.Models.TfmAdoptionResponse;
using TfmAdoptionSeries = NuGetTrends.Web.Client.Models.TfmAdoptionSeries;
using TfmAdoptionPoint = NuGetTrends.Web.Client.Models.TfmAdoptionPoint;

namespace NuGetTrends.Web.Tests;

/// <summary>
/// Verifies that the source-generated JSON serializer context
/// correctly round-trips every model type used by the WASM client.
/// This catches trimming issues before they appear at runtime.
/// </summary>
public class JsonSerializationTests
{
    [Fact]
    public void PackageSearchResult_RoundTrips()
    {
        var original = new List<PackageSearchResult>
        {
            new() { PackageId = "Sentry", DownloadCount = 12345, IconUrl = "https://example.com/icon.png" },
            new() { PackageId = "Newtonsoft.Json", DownloadCount = 999999 }
        };

        var json = JsonSerializer.Serialize(original, NuGetTrendsJsonContext.Default.ListPackageSearchResult);
        var deserialized = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.ListPackageSearchResult);

        deserialized.Should().NotBeNull();
        deserialized.Should().HaveCount(2);
        deserialized![0].PackageId.Should().Be("Sentry");
        deserialized[0].DownloadCount.Should().Be(12345);
        deserialized[0].IconUrl.Should().Be("https://example.com/icon.png");
        deserialized[1].PackageId.Should().Be("Newtonsoft.Json");
        deserialized[1].IconUrl.Should().Be("https://www.nuget.org/Content/gallery/img/default-package-icon.svg");
    }

    [Fact]
    public void PackageDownloadHistory_RoundTrips()
    {
        var original = new PackageDownloadHistory
        {
            Id = "Sentry",
            Downloads =
            [
                new DownloadStats { Week = new DateTime(2024, 1, 1), Count = 100 },
                new DownloadStats { Week = new DateTime(2024, 1, 8), Count = null }
            ],
            Color = "#ff0000"
        };

        var json = JsonSerializer.Serialize(original, NuGetTrendsJsonContext.Default.PackageDownloadHistory);
        var deserialized = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.PackageDownloadHistory);

        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be("Sentry");
        deserialized.Color.Should().Be("#ff0000");
        deserialized.Downloads.Should().HaveCount(2);
        deserialized.Downloads[0].Count.Should().Be(100);
        deserialized.Downloads[1].Count.Should().BeNull();
    }

    [Fact]
    public void TrendingPackage_RoundTrips()
    {
        var original = new List<TrendingPackage>
        {
            new()
            {
                PackageId = "HotChocolate",
                DownloadCount = 50000,
                GrowthRate = 0.25,
                IconUrl = "https://example.com/hc.png",
                GitHubUrl = "https://github.com/ChilliCream/hotchocolate"
            },
            new() { PackageId = "MediatR", DownloadCount = 30000 }
        };

        var json = JsonSerializer.Serialize(original, NuGetTrendsJsonContext.Default.ListTrendingPackage);
        var deserialized = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.ListTrendingPackage);

        deserialized.Should().NotBeNull();
        deserialized.Should().HaveCount(2);
        deserialized![0].PackageId.Should().Be("HotChocolate");
        deserialized[0].GrowthRate.Should().Be(0.25);
        deserialized[0].GitHubUrl.Should().Be("https://github.com/ChilliCream/hotchocolate");
        deserialized[1].GrowthRate.Should().BeNull();
        deserialized[1].GitHubUrl.Should().BeNull();
    }

    [Fact]
    public void TfmFamilyGroup_RoundTrips()
    {
        var original = new List<TfmFamilyGroup>
        {
            new() { Family = ".NET", Tfms = ["net8.0", "net9.0"] },
            new() { Family = ".NET Standard", Tfms = ["netstandard2.0"] }
        };

        var json = JsonSerializer.Serialize(original, NuGetTrendsJsonContext.Default.ListTfmFamilyGroup);
        var deserialized = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.ListTfmFamilyGroup);

        deserialized.Should().NotBeNull();
        deserialized.Should().HaveCount(2);
        deserialized![0].Family.Should().Be(".NET");
        deserialized[0].Tfms.Should().BeEquivalentTo(["net8.0", "net9.0"]);
    }

    [Fact]
    public void TfmAdoptionResponse_RoundTrips()
    {
        var original = new NuGetTrends.Web.Client.Models.TfmAdoptionResponse
        {
            Series =
            [
                new NuGetTrends.Web.Client.Models.TfmAdoptionSeries
                {
                    Tfm = "net8.0",
                    Family = ".NET",
                    DataPoints =
                    [
                        new TfmAdoptionPoint { Month = new DateOnly(2024, 1, 1), CumulativeCount = 500, NewCount = 500 },
                        new TfmAdoptionPoint { Month = new DateOnly(2024, 2, 1), CumulativeCount = 800, NewCount = 300 }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(original, NuGetTrendsJsonContext.Default.TfmAdoptionResponse);
        var deserialized = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.TfmAdoptionResponse);

        deserialized.Should().NotBeNull();
        deserialized!.Series.Should().HaveCount(1);
        deserialized.Series[0].Tfm.Should().Be("net8.0");
        deserialized.Series[0].DataPoints.Should().HaveCount(2);
        deserialized.Series[0].DataPoints[0].CumulativeCount.Should().Be(500);
        deserialized.Series[0].DataPoints[1].NewCount.Should().Be(300);
    }

    [Fact]
    public void CamelCaseNaming_IsUsed()
    {
        var original = new PackageDownloadHistory
        {
            Id = "Test",
            Downloads = [new DownloadStats { Week = new DateTime(2024, 1, 1), Count = 42 }]
        };

        var json = JsonSerializer.Serialize(original, NuGetTrendsJsonContext.Default.PackageDownloadHistory);

        json.Should().Contain("\"id\":");
        json.Should().Contain("\"downloads\":");
        json.Should().Contain("\"week\":");
        json.Should().Contain("\"count\":");
        json.Should().NotContain("\"Id\":");
        json.Should().NotContain("\"Downloads\":");
    }

    [Fact]
    public void Deserialization_FromServerCamelCaseJson()
    {
        // Simulate JSON as it would come from the ASP.NET Core API (camelCase by default)
        const string json = """
            {
                "id": "Sentry",
                "downloads": [
                    { "week": "2024-01-01T00:00:00", "count": 100 },
                    { "week": "2024-01-08T00:00:00", "count": 200 }
                ],
                "color": null
            }
            """;

        var result = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.PackageDownloadHistory);

        result.Should().NotBeNull();
        result!.Id.Should().Be("Sentry");
        result.Downloads.Should().HaveCount(2);
        result.Downloads[0].Count.Should().Be(100);
        result.Color.Should().BeNull();
    }

    [Fact]
    public void Deserialization_PackageSearchResult_FromServerJson()
    {
        const string json = """
            [
                { "packageId": "Sentry", "downloadCount": 12345, "iconUrl": "https://example.com/icon.png" },
                { "packageId": "Newtonsoft.Json", "downloadCount": 999 }
            ]
            """;

        var result = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.ListPackageSearchResult);

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![0].PackageId.Should().Be("Sentry");
        result[1].DownloadCount.Should().Be(999);
    }

    [Fact]
    public void Deserialization_TfmAdoptionResponse_FromServerJson()
    {
        const string json = """
            {
                "series": [
                    {
                        "tfm": "net8.0",
                        "family": ".NET",
                        "dataPoints": [
                            { "month": "2024-01-01", "cumulativeCount": 500, "newCount": 500 }
                        ]
                    }
                ]
            }
            """;

        var result = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.TfmAdoptionResponse);

        result.Should().NotBeNull();
        result!.Series.Should().HaveCount(1);
        result.Series[0].Tfm.Should().Be("net8.0");
        result.Series[0].DataPoints[0].CumulativeCount.Should().Be(500);
    }

    [Fact]
    public void EmptyList_RoundTrips()
    {
        var original = new List<PackageSearchResult>();

        var json = JsonSerializer.Serialize(original, NuGetTrendsJsonContext.Default.ListPackageSearchResult);
        var deserialized = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.ListPackageSearchResult);

        deserialized.Should().NotBeNull();
        deserialized.Should().BeEmpty();
    }

    [Fact]
    public void TrendingPackage_EmptyList_RoundTrips()
    {
        var original = new List<TrendingPackage>();

        var json = JsonSerializer.Serialize(original, NuGetTrendsJsonContext.Default.ListTrendingPackage);
        var deserialized = JsonSerializer.Deserialize(json, NuGetTrendsJsonContext.Default.ListTrendingPackage);

        deserialized.Should().NotBeNull();
        deserialized.Should().BeEmpty();
    }
}
