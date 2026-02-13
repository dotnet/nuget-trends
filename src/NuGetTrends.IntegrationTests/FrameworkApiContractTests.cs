using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using NuGetTrends.IntegrationTests.Infrastructure;
using NuGetTrends.Web.Client.Models;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.IntegrationTests;

/// <summary>
/// Tests that the framework API contract matches the Blazor client models.
/// </summary>
[Collection("E2E")]
public class FrameworkApiContractTests : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NuGetTrendsWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    public FrameworkApiContractTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        _factory = new NuGetTrendsWebApplicationFactory(_fixture);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AvailableApi_Returns200_WithEmptyTable()
    {
        // Act
        var response = await _client.GetAsync("/api/framework/available");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<TfmFamilyGroup>>(WebDefaults);
        result.Should().NotBeNull();
        // May be empty if no data has been loaded
    }

    [Fact]
    public async Task AdoptionApi_Returns200_WithEmptyTable()
    {
        // Act
        var response = await _client.GetAsync("/api/framework/adoption");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TfmAdoptionResponse>(WebDefaults);
        result.Should().NotBeNull();
        result!.Series.Should().NotBeNull();
    }

    [Fact]
    public async Task AdoptionApi_WithFilter_Returns200()
    {
        // Act
        var response = await _client.GetAsync("/api/framework/adoption?tfms=net8.0,net9.0&families=.NET");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AvailableApi_JsonPropertyNames_MatchClientModel()
    {
        // Seed some data first via ClickHouse
        await SeedTfmAdoptionDataAsync();

        // Act
        var response = await _client.GetAsync("/api/framework/available");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Available TFMs JSON: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Array);

        if (root.GetArrayLength() > 0)
        {
            var first = root[0];
            first.TryGetProperty("family", out _).Should().BeTrue(
                "API must return 'family' (maps to TfmFamilyGroup.Family)");
            first.TryGetProperty("tfms", out _).Should().BeTrue(
                "API must return 'tfms' (maps to TfmFamilyGroup.Tfms)");
        }
    }

    [Fact]
    public async Task AdoptionApi_JsonPropertyNames_MatchClientModel()
    {
        await SeedTfmAdoptionDataAsync();

        // Act
        var response = await _client.GetAsync("/api/framework/adoption");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Adoption JSON: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("series", out var seriesElement).Should().BeTrue(
            "API must return 'series' (maps to TfmAdoptionResponse.Series)");

        if (seriesElement.GetArrayLength() > 0)
        {
            var first = seriesElement[0];
            first.TryGetProperty("tfm", out _).Should().BeTrue(
                "API must return 'tfm' (maps to TfmAdoptionSeries.Tfm)");
            first.TryGetProperty("family", out _).Should().BeTrue(
                "API must return 'family' (maps to TfmAdoptionSeries.Family)");
            first.TryGetProperty("dataPoints", out var dpElement).Should().BeTrue(
                "API must return 'dataPoints' (maps to TfmAdoptionSeries.DataPoints)");

            if (dpElement.GetArrayLength() > 0)
            {
                var dp = dpElement[0];
                dp.TryGetProperty("month", out _).Should().BeTrue(
                    "API must return 'month' (maps to TfmAdoptionPoint.Month)");
                dp.TryGetProperty("cumulativeCount", out _).Should().BeTrue(
                    "API must return 'cumulativeCount' (maps to TfmAdoptionPoint.CumulativeCount)");
                dp.TryGetProperty("newCount", out _).Should().BeTrue(
                    "API must return 'newCount' (maps to TfmAdoptionPoint.NewCount)");
            }
        }
    }

    private async Task SeedTfmAdoptionDataAsync()
    {
        var clickHouseService = _fixture.CreateClickHouseService();
        var dataPoints = new List<NuGetTrends.Data.TfmAdoptionDataPoint>
        {
            new()
            {
                Month = new DateOnly(2024, 1, 1),
                Tfm = "net8.0",
                Family = ".NET",
                NewPackageCount = 100,
                CumulativePackageCount = 100
            }
        };
        await clickHouseService.InsertTfmAdoptionSnapshotAsync(dataPoints);
    }
}
