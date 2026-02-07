using Xunit;
using FluentAssertions;
using NuGetTrends.Web.Client.Models;

namespace NuGetTrends.Web.Tests;

public class PackageModelsTests
{
    [Fact]
    public void PackageSearchResult_HasDefaultIconUrl()
    {
        // Act
        var result = new PackageSearchResult { PackageId = "Test" };

        // Assert
        result.IconUrl.Should().Be("https://www.nuget.org/Content/gallery/img/default-package-icon.svg");
    }

    [Fact]
    public void PackageSearchResult_CanSetAllProperties()
    {
        // Arrange
        var customIconUrl = "https://example.com/icon.png";

        // Act
        var result = new PackageSearchResult
        {
            PackageId = "TestPackage",
            DownloadCount = 12345,
            IconUrl = customIconUrl
        };

        // Assert
        result.PackageId.Should().Be("TestPackage");
        result.DownloadCount.Should().Be(12345);
        result.IconUrl.Should().Be(customIconUrl);
    }

    [Fact]
    public void PackageDownloadHistory_CanSetColor()
    {
        // Arrange
        var history = new PackageDownloadHistory
        {
            Id = "Test",
            Downloads = []
        };

        // Act
        history.Color = "#ff0000";

        // Assert
        history.Color.Should().Be("#ff0000");
    }

    [Fact]
    public void DownloadStats_CanHaveNullCount()
    {
        // Act
        var stats = new DownloadStats
        {
            Week = DateTime.UtcNow,
            Count = null
        };

        // Assert
        stats.Count.Should().BeNull();
    }

    [Fact]
    public void TrendingPackage_HasDefaultIconUrl()
    {
        // Act
        var package = new TrendingPackage { PackageId = "Test" };

        // Assert
        package.IconUrl.Should().Be("https://www.nuget.org/Content/gallery/img/default-package-icon.svg");
        package.GitHubUrl.Should().BeNull();
    }

    [Fact]
    public void TrendingPackage_CanSetAllProperties()
    {
        // Act
        var package = new TrendingPackage
        {
            PackageId = "TestPackage",
            DownloadCount = 999999,
            GrowthRate = 0.25,
            IconUrl = "https://example.com/icon.png",
            GitHubUrl = "https://github.com/owner/repo"
        };

        // Assert
        package.PackageId.Should().Be("TestPackage");
        package.DownloadCount.Should().Be(999999);
        package.GrowthRate.Should().Be(0.25);
        package.IconUrl.Should().Be("https://example.com/icon.png");
        package.GitHubUrl.Should().Be("https://github.com/owner/repo");
    }

    [Fact]
    public void PackageColor_RequiresIdAndColor()
    {
        // Act
        var packageColor = new PackageColor
        {
            Id = "TestPackage",
            Color = "#055499"
        };

        // Assert
        packageColor.Id.Should().Be("TestPackage");
        packageColor.Color.Should().Be("#055499");
    }

    [Fact]
    public void SearchPeriod_RequiresTextAndValue()
    {
        // Act
        var period = new SearchPeriod
        {
            Text = "6 months",
            Value = 6
        };

        // Assert
        period.Text.Should().Be("6 months");
        period.Value.Should().Be(6);
    }

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void ThemePreference_HasExpectedValues(ThemePreference preference)
    {
        // Assert
        Enum.IsDefined(typeof(ThemePreference), preference).Should().BeTrue();
    }
}
