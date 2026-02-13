using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NuGetTrends.Data.ClickHouse;
using NuGetTrends.Data.Tests.Infrastructure;
using Xunit;

namespace NuGetTrends.Data.Tests;

[Collection("ClickHouse")]
public class TfmAdoptionTests : IAsyncLifetime
{
    private readonly ClickHouseFixture _fixture;
    private readonly ClickHouseService _sut;

    public TfmAdoptionTests(ClickHouseFixture fixture)
    {
        _fixture = fixture;
        var connectionInfo = ClickHouseConnectionInfo.Parse(fixture.ConnectionString);
        _sut = new ClickHouseService(fixture.ConnectionString, NullLogger<ClickHouseService>.Instance, connectionInfo, null);
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetTableAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InsertAndRead_RoundTrip_ReturnsCorrectData()
    {
        // Arrange
        var dataPoints = new List<TfmAdoptionDataPoint>
        {
            new()
            {
                Month = new DateOnly(2024, 1, 1),
                Tfm = "net8.0",
                Family = ".NET",
                NewPackageCount = 100,
                CumulativePackageCount = 100
            },
            new()
            {
                Month = new DateOnly(2024, 2, 1),
                Tfm = "net8.0",
                Family = ".NET",
                NewPackageCount = 50,
                CumulativePackageCount = 150
            },
            new()
            {
                Month = new DateOnly(2024, 1, 1),
                Tfm = "netstandard2.0",
                Family = ".NET Standard",
                NewPackageCount = 200,
                CumulativePackageCount = 200
            }
        };

        // Act
        var insertCount = await _sut.InsertTfmAdoptionSnapshotAsync(dataPoints);

        // Assert
        insertCount.Should().Be(3);

        var result = await _sut.GetTfmAdoptionFromSnapshotAsync();
        result.Should().HaveCount(3);

        var net8Jan = result.First(r => r.Tfm == "net8.0" && r.Month == new DateOnly(2024, 1, 1));
        net8Jan.Family.Should().Be(".NET");
        net8Jan.NewPackageCount.Should().Be(100);
        net8Jan.CumulativePackageCount.Should().Be(100);

        var net8Feb = result.First(r => r.Tfm == "net8.0" && r.Month == new DateOnly(2024, 2, 1));
        net8Feb.CumulativePackageCount.Should().Be(150);
    }

    [Fact]
    public async Task GetTfmAdoptionFromSnapshot_EmptyTable_ReturnsEmptyList()
    {
        var result = await _sut.GetTfmAdoptionFromSnapshotAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTfmAdoptionFromSnapshot_FilterByTfm_ReturnsOnlyMatching()
    {
        // Arrange
        var dataPoints = new List<TfmAdoptionDataPoint>
        {
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "net8.0", Family = ".NET", NewPackageCount = 100, CumulativePackageCount = 100 },
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "net9.0", Family = ".NET", NewPackageCount = 50, CumulativePackageCount = 50 },
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "netstandard2.0", Family = ".NET Standard", NewPackageCount = 200, CumulativePackageCount = 200 }
        };
        await _sut.InsertTfmAdoptionSnapshotAsync(dataPoints);

        // Act
        var result = await _sut.GetTfmAdoptionFromSnapshotAsync(tfms: ["net8.0"]);

        // Assert
        result.Should().HaveCount(1);
        result[0].Tfm.Should().Be("net8.0");
    }

    [Fact]
    public async Task GetTfmAdoptionFromSnapshot_FilterByFamily_ReturnsOnlyMatching()
    {
        // Arrange
        var dataPoints = new List<TfmAdoptionDataPoint>
        {
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "net8.0", Family = ".NET", NewPackageCount = 100, CumulativePackageCount = 100 },
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "netstandard2.0", Family = ".NET Standard", NewPackageCount = 200, CumulativePackageCount = 200 }
        };
        await _sut.InsertTfmAdoptionSnapshotAsync(dataPoints);

        // Act
        var result = await _sut.GetTfmAdoptionFromSnapshotAsync(families: [".NET Standard"]);

        // Assert
        result.Should().HaveCount(1);
        result[0].Tfm.Should().Be("netstandard2.0");
    }

    [Fact]
    public async Task InsertTfmAdoptionSnapshot_IdempotentRetry_DeletesAndReinserts()
    {
        // Arrange - insert initial data
        var dataPoints = new List<TfmAdoptionDataPoint>
        {
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "net8.0", Family = ".NET", NewPackageCount = 100, CumulativePackageCount = 100 }
        };
        await _sut.InsertTfmAdoptionSnapshotAsync(dataPoints);

        // Act - re-insert with updated data for same month
        var updatedDataPoints = new List<TfmAdoptionDataPoint>
        {
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "net8.0", Family = ".NET", NewPackageCount = 150, CumulativePackageCount = 150 }
        };
        await _sut.InsertTfmAdoptionSnapshotAsync(updatedDataPoints);

        // Assert - should have the updated data (DELETE + INSERT pattern)
        var result = await _sut.GetTfmAdoptionFromSnapshotAsync();
        result.Should().HaveCount(1);
        result[0].NewPackageCount.Should().Be(150);
    }

    [Fact]
    public async Task GetAvailableTfms_ReturnsGroupedFamilies()
    {
        // Arrange
        var dataPoints = new List<TfmAdoptionDataPoint>
        {
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "net8.0", Family = ".NET", NewPackageCount = 100, CumulativePackageCount = 100 },
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "net9.0", Family = ".NET", NewPackageCount = 50, CumulativePackageCount = 50 },
            new() { Month = new DateOnly(2024, 1, 1), Tfm = "netstandard2.0", Family = ".NET Standard", NewPackageCount = 200, CumulativePackageCount = 200 }
        };
        await _sut.InsertTfmAdoptionSnapshotAsync(dataPoints);

        // Act
        var result = await _sut.GetAvailableTfmsAsync();

        // Assert
        result.Should().HaveCount(2);

        var dotNet = result.First(g => g.Family == ".NET");
        dotNet.Tfms.Should().Contain("net8.0");
        dotNet.Tfms.Should().Contain("net9.0");

        var standard = result.First(g => g.Family == ".NET Standard");
        standard.Tfms.Should().Contain("netstandard2.0");
    }

    [Fact]
    public async Task GetAvailableTfms_EmptyTable_ReturnsEmptyList()
    {
        var result = await _sut.GetAvailableTfmsAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task InsertTfmAdoptionSnapshot_EmptyList_ReturnsZero()
    {
        var result = await _sut.InsertTfmAdoptionSnapshotAsync([]);
        result.Should().Be(0);
    }
}
