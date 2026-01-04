using FluentAssertions;
using NuGetTrends.Data;
using Xunit;

namespace NuGetTrends.Data.Tests;

/// <summary>
/// Tests for the TrendingPackage model.
/// </summary>
public class TrendingPackageCacheTests
{
    [Theory]
    [InlineData(1000, 2000, 1.0)]    // 100% growth
    [InlineData(2000, 3000, 0.5)]    // 50% growth
    [InlineData(1000, 1000, 0.0)]    // 0% growth
    [InlineData(2000, 1000, -0.5)]   // -50% growth (decline)
    [InlineData(4000, 5000, 0.25)]   // 25% growth
    public void GrowthRate_CalculatesCorrectly(long previous, long current, double expectedGrowth)
    {
        // Arrange
        var package = new TrendingPackage
        {
            PackageId = "test-package",
            PreviousWeekDownloads = previous,
            CurrentWeekDownloads = current
        };

        // Assert
        package.GrowthRate.Should().BeApproximately(expectedGrowth, 0.001);
    }

    [Fact]
    public void GrowthRate_WithZeroPreviousDownloads_ReturnsNull()
    {
        // Arrange
        var package = new TrendingPackage
        {
            PackageId = "new-package",
            PreviousWeekDownloads = 0,
            CurrentWeekDownloads = 1000
        };

        // Assert
        package.GrowthRate.Should().BeNull();
    }

    [Fact]
    public void GrowthRate_WithBothZero_ReturnsNull()
    {
        // Arrange
        var package = new TrendingPackage
        {
            PackageId = "empty-package",
            PreviousWeekDownloads = 0,
            CurrentWeekDownloads = 0
        };

        // Assert
        package.GrowthRate.Should().BeNull();
    }
}
