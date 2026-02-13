using Bunit;
using FluentAssertions;
using NuGetTrends.Web.Client.Models;
using NuGetTrends.Web.Client.Shared;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class TfmFilterTests : TestContext
{
    private static List<TfmFamilyGroup> CreateTestGroups() =>
    [
        new TfmFamilyGroup { Family = ".NET", Tfms = ["net8.0", "net9.0"] },
        new TfmFamilyGroup { Family = ".NET Standard", Tfms = ["netstandard2.0", "netstandard2.1"] }
    ];

    [Fact]
    public void Renders_DropdownButton_WithSelectedCount()
    {
        var selectedTfms = new HashSet<string> { "net8.0", "net9.0" };

        var cut = RenderComponent<TfmFilter>(parameters => parameters
            .Add(p => p.Groups, CreateTestGroups())
            .Add(p => p.SelectedTfms, selectedTfms));

        var button = cut.Find("button");
        button.TextContent.Should().Contain("2 TFMs selected");
    }

    [Fact]
    public void DropdownIsClosedByDefault()
    {
        var cut = RenderComponent<TfmFilter>(parameters => parameters
            .Add(p => p.Groups, CreateTestGroups())
            .Add(p => p.SelectedTfms, new HashSet<string>()));

        var dropdown = cut.Find(".tfm-filter-dropdown");
        dropdown.ClassList.Should().NotContain("is-active");
    }

    [Fact]
    public void ClickButton_OpensDropdown()
    {
        var cut = RenderComponent<TfmFilter>(parameters => parameters
            .Add(p => p.Groups, CreateTestGroups())
            .Add(p => p.SelectedTfms, new HashSet<string>()));

        cut.Find("button").Click();

        var dropdown = cut.Find(".tfm-filter-dropdown");
        dropdown.ClassList.Should().Contain("is-active");
    }

    [Fact]
    public void ShowsFamilyGroupHeaders()
    {
        var cut = RenderComponent<TfmFilter>(parameters => parameters
            .Add(p => p.Groups, CreateTestGroups())
            .Add(p => p.SelectedTfms, new HashSet<string>()));

        // Open dropdown
        cut.Find("button").Click();

        var headers = cut.FindAll(".tfm-filter-group-header strong");
        headers.Should().HaveCount(2);
        headers[0].TextContent.Should().Be(".NET");
        headers[1].TextContent.Should().Be(".NET Standard");
    }

    [Fact]
    public void CheckboxReflectsSelectedState()
    {
        var selectedTfms = new HashSet<string> { "net8.0" };

        var cut = RenderComponent<TfmFilter>(parameters => parameters
            .Add(p => p.Groups, CreateTestGroups())
            .Add(p => p.SelectedTfms, selectedTfms));

        // Open dropdown
        cut.Find("button").Click();

        var checkboxes = cut.FindAll("input[type='checkbox']");
        // net8.0 should be checked, net9.0 should not
        checkboxes[0].HasAttribute("checked").Should().BeTrue();
        checkboxes[1].HasAttribute("checked").Should().BeFalse();
    }
}
