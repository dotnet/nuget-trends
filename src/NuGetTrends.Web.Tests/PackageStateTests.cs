using Xunit;
using FluentAssertions;
using NuGetTrends.Web.Models;
using NuGetTrends.Web.Services;

namespace NuGetTrends.Web.Tests;

public class PackageStateTests
{
    [Fact]
    public void AddPackage_WhenChartIsEmpty_AssignsFirstColor()
    {
        // Arrange
        var state = new PackageState();
        var history = CreateHistory("TestPackage");

        // Act
        var color = state.AddPackage(history);

        // Assert
        color.Should().Be("#055499"); // First color in the palette
        state.Packages.Should().HaveCount(1);
        state.Packages[0].Id.Should().Be("TestPackage");
        state.Packages[0].Color.Should().Be("#055499");
    }

    [Fact]
    public void AddPackage_WhenChartHasPackages_AssignsNextAvailableColor()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("Package1"));

        // Act
        var color = state.AddPackage(CreateHistory("Package2"));

        // Assert
        color.Should().Be("#ff5a00"); // Second color in the palette
        state.Packages.Should().HaveCount(2);
    }

    [Fact]
    public void AddPackage_WhenChartIsFull_ReturnsNull()
    {
        // Arrange
        var state = new PackageState();
        for (int i = 0; i < PackageState.MaxChartItems; i++)
        {
            state.AddPackage(CreateHistory($"Package{i}"));
        }

        // Act
        var color = state.AddPackage(CreateHistory("ExtraPackage"));

        // Assert
        color.Should().BeNull();
        state.Packages.Should().HaveCount(PackageState.MaxChartItems);
    }

    [Fact]
    public void AddPackage_WhenPackageAlreadyExists_ReturnsNull()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("TestPackage"));

        // Act
        var color = state.AddPackage(CreateHistory("TestPackage"));

        // Assert
        color.Should().BeNull();
        state.Packages.Should().HaveCount(1);
    }

    [Fact]
    public void AddPackage_IsCaseInsensitive_ForDuplicateCheck()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("TestPackage"));

        // Act
        var color = state.AddPackage(CreateHistory("TESTPACKAGE"));

        // Assert
        color.Should().BeNull();
        state.Packages.Should().HaveCount(1);
    }

    [Fact]
    public void AddPackage_RaisesPackagePlottedEvent()
    {
        // Arrange
        var state = new PackageState();
        PackageDownloadHistory? eventHistory = null;
        state.PackagePlotted += (_, h) => eventHistory = h;
        var history = CreateHistory("TestPackage");

        // Act
        state.AddPackage(history);

        // Assert
        eventHistory.Should().NotBeNull();
        eventHistory!.Id.Should().Be("TestPackage");
        eventHistory.Color.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RemovePackage_RemovesPackageFromList()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("TestPackage"));

        // Act
        state.RemovePackage("TestPackage");

        // Assert
        state.Packages.Should().BeEmpty();
    }

    [Fact]
    public void RemovePackage_IsCaseInsensitive()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("TestPackage"));

        // Act
        state.RemovePackage("TESTPACKAGE");

        // Assert
        state.Packages.Should().BeEmpty();
    }

    [Fact]
    public void RemovePackage_FreesColorForReuse()
    {
        // Arrange
        var state = new PackageState();
        var firstColor = state.AddPackage(CreateHistory("Package1"));
        state.AddPackage(CreateHistory("Package2"));

        // Act
        state.RemovePackage("Package1");
        var reusedColor = state.AddPackage(CreateHistory("Package3"));

        // Assert
        reusedColor.Should().Be(firstColor);
    }

    [Fact]
    public void RemovePackage_RaisesPackageRemovedEvent()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("TestPackage"));
        string? removedId = null;
        state.PackageRemoved += (_, id) => removedId = id;

        // Act
        state.RemovePackage("TestPackage");

        // Assert
        removedId.Should().Be("TestPackage");
    }

    [Fact]
    public void RemovePackage_WhenPackageNotFound_DoesNothing()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("TestPackage"));
        bool eventRaised = false;
        state.PackageRemoved += (_, _) => eventRaised = true;

        // Act
        state.RemovePackage("NonExistent");

        // Assert
        state.Packages.Should().HaveCount(1);
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void UpdatePackage_WhenPackageExists_RaisesPackagePlottedEvent()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("TestPackage"));
        PackageDownloadHistory? eventHistory = null;
        state.PackagePlotted += (_, h) => eventHistory = h;

        var updatedHistory = CreateHistory("TestPackage", downloadCount: 999);

        // Act
        state.UpdatePackage(updatedHistory);

        // Assert
        eventHistory.Should().NotBeNull();
        eventHistory!.Downloads.Should().HaveCount(1);
        eventHistory.Downloads[0].Count.Should().Be(999);
    }

    [Fact]
    public void UpdatePackage_PreservesExistingColor()
    {
        // Arrange
        var state = new PackageState();
        var originalColor = state.AddPackage(CreateHistory("TestPackage"));
        PackageDownloadHistory? eventHistory = null;
        state.PackagePlotted += (_, h) => eventHistory = h;

        var updatedHistory = CreateHistory("TestPackage");

        // Act
        state.UpdatePackage(updatedHistory);

        // Assert
        eventHistory!.Color.Should().Be(originalColor);
    }

    [Fact]
    public void UpdatePackage_WhenPackageNotFound_DoesNothing()
    {
        // Arrange
        var state = new PackageState();
        bool eventRaised = false;
        state.PackagePlotted += (_, _) => eventRaised = true;

        // Act
        state.UpdatePackage(CreateHistory("NonExistent"));

        // Assert
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void SearchPeriod_DefaultsToInitialValue()
    {
        // Arrange & Act
        var state = new PackageState();

        // Assert
        state.SearchPeriod.Should().Be(SearchPeriods.Initial.Value);
    }

    [Fact]
    public void SearchPeriod_WhenChanged_RaisesEvent()
    {
        // Arrange
        var state = new PackageState();
        int? newPeriod = null;
        state.SearchPeriodChanged += (_, p) => newPeriod = p;

        // Act
        state.SearchPeriod = 12;

        // Assert
        newPeriod.Should().Be(12);
    }

    [Fact]
    public void SearchPeriod_WhenSetToSameValue_DoesNotRaiseEvent()
    {
        // Arrange
        var state = new PackageState();
        state.SearchPeriod = 12;
        bool eventRaised = false;
        state.SearchPeriodChanged += (_, _) => eventRaised = true;

        // Act
        state.SearchPeriod = 12;

        // Assert
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void HasPackage_ReturnsTrueWhenPackageExists()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("TestPackage"));

        // Act & Assert
        state.HasPackage("TestPackage").Should().BeTrue();
        state.HasPackage("TESTPACKAGE").Should().BeTrue(); // Case insensitive
    }

    [Fact]
    public void HasPackage_ReturnsFalseWhenPackageNotFound()
    {
        // Arrange
        var state = new PackageState();

        // Act & Assert
        state.HasPackage("NonExistent").Should().BeFalse();
    }

    [Fact]
    public void GetPackageColor_ReturnsColorWhenPackageExists()
    {
        // Arrange
        var state = new PackageState();
        var expectedColor = state.AddPackage(CreateHistory("TestPackage"));

        // Act
        var color = state.GetPackageColor("TestPackage");

        // Assert
        color.Should().Be(expectedColor);
    }

    [Fact]
    public void GetPackageColor_ReturnsNullWhenPackageNotFound()
    {
        // Arrange
        var state = new PackageState();

        // Act
        var color = state.GetPackageColor("NonExistent");

        // Assert
        color.Should().BeNull();
    }

    [Fact]
    public void Clear_RemovesAllPackages()
    {
        // Arrange
        var state = new PackageState();
        state.AddPackage(CreateHistory("Package1"));
        state.AddPackage(CreateHistory("Package2"));

        var removedIds = new List<string>();
        state.PackageRemoved += (_, id) => removedIds.Add(id);

        // Act
        state.Clear();

        // Assert
        state.Packages.Should().BeEmpty();
        removedIds.Should().HaveCount(2);
    }

    private static PackageDownloadHistory CreateHistory(string id, long downloadCount = 100)
    {
        return new PackageDownloadHistory
        {
            Id = id,
            Downloads = [new DownloadStats { Week = DateTime.UtcNow, Count = downloadCount }]
        };
    }
}
