using Xunit;
using FluentAssertions;
using NuGetTrends.Web.Client.Models;

namespace NuGetTrends.Web.Tests;

public class SearchPeriodsTests
{
    [Fact]
    public void Default_ContainsExpectedPeriods()
    {
        // Act
        var periods = SearchPeriods.Default;

        // Assert
        periods.Should().HaveCount(7);
        periods[0].Value.Should().Be(3);
        periods[0].Text.Should().Be("3 months");
        periods[1].Value.Should().Be(6);
        periods[1].Text.Should().Be("6 months");
        periods[2].Value.Should().Be(12);
        periods[2].Text.Should().Be("1 year");
        periods[3].Value.Should().Be(24);
        periods[3].Text.Should().Be("2 years");
        periods[4].Value.Should().Be(60);
        periods[4].Text.Should().Be("5 years");
        periods[5].Value.Should().Be(120);
        periods[5].Text.Should().Be("10 years");
        periods[6].Text.Should().Be("All time");
    }

    [Fact]
    public void Initial_Is24Months()
    {
        // Act
        var initial = SearchPeriods.Initial;

        // Assert
        initial.Value.Should().Be(24);
        initial.Text.Should().Be("2 years");
    }

    [Fact]
    public void AllTime_CalculatesMonthsFromJanuary2012()
    {
        // Arrange
        var dataStartDate = new DateTime(2012, 1, 1);
        var now = DateTime.UtcNow;
        var expectedMonths = (now.Year - dataStartDate.Year) * 12 + (now.Month - dataStartDate.Month);

        // Act
        var allTimePeriod = SearchPeriods.Default.Last();

        // Assert
        allTimePeriod.Text.Should().Be("All time");
        // Allow some tolerance since the test might run at month boundary
        allTimePeriod.Value.Should().BeInRange(expectedMonths - 1, expectedMonths + 1);
    }
}
