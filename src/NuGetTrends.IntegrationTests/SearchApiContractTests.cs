using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.IntegrationTests.Infrastructure;
using NuGetTrends.Web.Client.Models;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.IntegrationTests;

/// <summary>
/// Tests that the search API contract matches the Blazor client models.
/// These tests validate the full search-to-chart flow used by the SearchInput component:
///   1. Search API returns results deserializable into <see cref="PackageSearchResult"/>
///   2. History API returns results deserializable into <see cref="PackageDownloadHistory"/>
///   3. JSON property names match between server and client
/// </summary>
[Collection("E2E")]
public class SearchApiContractTests : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NuGetTrendsWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    // Use Web defaults (camelCase, case-insensitive) to match what Blazor's
    // HttpClient.GetFromJsonAsync uses internally.
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    public SearchApiContractTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

        // Ensure catalog entries exist (may have been deleted by the deletion test)
        await _fixture.RestoreCatalogEntriesAsync();

        // Seed package_downloads + ClickHouse data if empty so these tests
        // don't depend on E2E tests running first.
        await using var context = _fixture.CreateDbContext();
        var hasDownloads = await context.PackageDownloads.AnyAsync();
        if (!hasDownloads)
        {
            var clickHouseService = _fixture.CreateClickHouseService();
            await TestDataSeeder.SeedHistoricalDownloadsAsync(
                context, clickHouseService, _fixture.ImportedPackages);
        }

        _factory = new NuGetTrendsWebApplicationFactory(_fixture);
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SearchApi_ReturnsResults_DeserializableAsPackageSearchResult()
    {
        // Arrange — use the first imported package as the search query
        var package = _fixture.ImportedPackages.First();

        // Act — call the search API exactly as the SearchInput component does
        var results = await _client.GetFromJsonAsync<List<PackageSearchResult>>(
            $"/api/package/search?q={Uri.EscapeDataString(package.PackageId)}",
            WebDefaults);

        // Assert — results should deserialize correctly and contain the searched package
        results.Should().NotBeNull();
        results.Should().NotBeEmpty("search should find the imported package");

        var match = results!.FirstOrDefault(r =>
            r.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase));

        match.Should().NotBeNull($"search results should contain '{package.PackageId}'");
        match!.PackageId.Should().NotBeNullOrEmpty();
        match.IconUrl.Should().NotBeNullOrEmpty("icon URL should have a default value");

        _output.WriteLine($"Search for '{package.PackageId}': found with DownloadCount={match.DownloadCount}");
    }

    [Fact]
    public async Task SearchApi_JsonPropertyNames_MatchClientModel()
    {
        // Arrange
        var package = _fixture.ImportedPackages.First();

        // Act — get raw JSON to inspect property names
        var response = await _client.GetAsync(
            $"/api/package/search?q={Uri.EscapeDataString(package.PackageId)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Raw JSON: {json}");

        using var doc = JsonDocument.Parse(json);
        var firstResult = doc.RootElement.EnumerateArray().First();

        // Assert — verify exact camelCase property names that the client expects
        firstResult.TryGetProperty("packageId", out _).Should().BeTrue(
            "API must return 'packageId' (maps to PackageSearchResult.PackageId)");
        firstResult.TryGetProperty("downloadCount", out _).Should().BeTrue(
            "API must return 'downloadCount' (maps to PackageSearchResult.DownloadCount)");
        firstResult.TryGetProperty("iconUrl", out _).Should().BeTrue(
            "API must return 'iconUrl' (maps to PackageSearchResult.IconUrl)");

        // Verify the old property name is NOT present
        firstResult.TryGetProperty("latestDownloadCount", out _).Should().BeFalse(
            "API must NOT return 'latestDownloadCount' — client expects 'downloadCount'");
    }

    [Fact]
    public async Task HistoryApi_ReturnsData_DeserializableAsPackageDownloadHistory()
    {
        // Arrange — use the first imported package
        var package = _fixture.ImportedPackages.First();

        // Act — call the history API exactly as the SearchInput.SelectPackage method does
        var history = await _client.GetFromJsonAsync<PackageDownloadHistory>(
            $"/api/package/history/{Uri.EscapeDataString(package.PackageId)}?months=3",
            WebDefaults);

        // Assert
        history.Should().NotBeNull();
        history!.Id.Should().Be(package.PackageId);
        history.Downloads.Should().NotBeNull();
        history.Downloads.Should().NotBeEmpty("package should have download history");

        foreach (var week in history.Downloads.Take(3))
        {
            _output.WriteLine($"  Week {week.Week:yyyy-MM-dd}: {week.Count:N0} downloads");
        }
    }

    [Fact]
    public async Task HistoryApi_NonExistentPackage_Returns404()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/package/history/ThisPackageDoesNotExist12345?months=3");

        // Assert — SearchInput catches 404 and shows "package doesn't exist" toast
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SearchThenHistory_FullDropdownFlow_WorksEndToEnd()
    {
        // This test simulates the full flow that SearchInput.razor performs:
        // 1. User types → search API called
        // 2. User selects result → history API called
        // 3. History data is used to render the chart

        // Step 1: Search
        var package = _fixture.ImportedPackages.First();
        var searchResults = await _client.GetFromJsonAsync<List<PackageSearchResult>>(
            $"/api/package/search?q={Uri.EscapeDataString(package.PackageId)}",
            WebDefaults);

        searchResults.Should().NotBeNull();
        searchResults.Should().NotBeEmpty();

        var selected = searchResults!.First(r =>
            r.PackageId.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase));

        _output.WriteLine($"Step 1 - Search found: {selected.PackageId} ({selected.DownloadCount:N0} downloads)");

        // Step 2: Fetch history (as SelectPackage does)
        var history = await _client.GetFromJsonAsync<PackageDownloadHistory>(
            $"/api/package/history/{Uri.EscapeDataString(selected.PackageId)}?months=24",
            WebDefaults);

        history.Should().NotBeNull();
        history!.Id.Should().Be(selected.PackageId);
        history.Downloads.Should().NotBeEmpty();

        _output.WriteLine($"Step 2 - History: {history.Downloads.Count} weeks of data");

        // Step 3: Verify data can be used for charting (non-null counts, valid dates)
        var validWeeks = history.Downloads.Where(d => d.Count.HasValue).ToList();
        validWeeks.Should().NotBeEmpty("chart needs at least one data point");

        _output.WriteLine($"Step 3 - Chart data: {validWeeks.Count} weeks with download counts");
    }

    [Fact]
    public async Task SearchApi_EmptyQuery_ReturnsEmptyList()
    {
        var results = await _client.GetFromJsonAsync<List<PackageSearchResult>>(
            "/api/package/search?q=zzzznonexistentpackagezzz",
            WebDefaults);

        results.Should().NotBeNull();
        results.Should().BeEmpty("no packages should match a nonsense query");
    }
}
