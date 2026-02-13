using Xunit;
using FluentAssertions;
using ClientModels = NuGetTrends.Web.Client.Models;

namespace NuGetTrends.Web.Tests;

public class FrameworkModelsTests
{
    [Fact]
    public void TfmAdoptionResponse_CanSetSeries()
    {
        var response = new ClientModels.TfmAdoptionResponse
        {
            Series =
            [
                new ClientModels.TfmAdoptionSeries
                {
                    Tfm = "net8.0",
                    Family = ".NET",
                    DataPoints =
                    [
                        new ClientModels.TfmAdoptionPoint { Month = new DateOnly(2024, 1, 1), CumulativeCount = 100, NewCount = 100 }
                    ]
                }
            ]
        };

        response.Series.Should().HaveCount(1);
        response.Series[0].Tfm.Should().Be("net8.0");
        response.Series[0].Family.Should().Be(".NET");
        response.Series[0].DataPoints.Should().HaveCount(1);
    }

    [Fact]
    public void TfmFamilyGroup_CanSetProperties()
    {
        var group = new ClientModels.TfmFamilyGroup
        {
            Family = ".NET",
            Tfms = ["net8.0", "net9.0"]
        };

        group.Family.Should().Be(".NET");
        group.Tfms.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(ClientModels.TfmViewMode.Family)]
    [InlineData(ClientModels.TfmViewMode.Individual)]
    public void TfmViewMode_HasExpectedValues(ClientModels.TfmViewMode mode)
    {
        Enum.IsDefined(typeof(ClientModels.TfmViewMode), mode).Should().BeTrue();
    }

    [Theory]
    [InlineData(ClientModels.TfmTimeMode.Absolute)]
    [InlineData(ClientModels.TfmTimeMode.Relative)]
    public void TfmTimeMode_HasExpectedValues(ClientModels.TfmTimeMode mode)
    {
        Enum.IsDefined(typeof(ClientModels.TfmTimeMode), mode).Should().BeTrue();
    }

    [Fact]
    public void TfmAdoptionPoint_DefaultValues()
    {
        var point = new ClientModels.TfmAdoptionPoint();

        point.Month.Should().Be(DateOnly.MinValue);
        point.CumulativeCount.Should().Be(0u);
        point.NewCount.Should().Be(0u);
    }

    [Fact]
    public void TfmAdoptionSeries_CanHaveEmptyDataPoints()
    {
        var series = new ClientModels.TfmAdoptionSeries
        {
            Tfm = "net8.0",
            Family = ".NET",
            DataPoints = []
        };

        series.DataPoints.Should().BeEmpty();
    }
}
