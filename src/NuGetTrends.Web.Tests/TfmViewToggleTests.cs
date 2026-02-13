using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NuGetTrends.Web.Client.Models;
using NuGetTrends.Web.Client.Shared;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class TfmViewToggleTests : TestContext
{
    [Fact]
    public void Renders_TwoButtons()
    {
        var cut = RenderComponent<TfmViewToggle>();
        var buttons = cut.FindAll("button");
        buttons.Should().HaveCount(2);
    }

    [Fact]
    public void FamilyButton_HasActiveClass_WhenFamilySelected()
    {
        var cut = RenderComponent<TfmViewToggle>(parameters => parameters
            .Add(p => p.ViewMode, TfmViewMode.Family));

        var buttons = cut.FindAll("button");
        buttons[0].ClassList.Should().Contain("is-primary");
        buttons[1].ClassList.Should().NotContain("is-primary");
    }

    [Fact]
    public void IndividualButton_HasActiveClass_WhenIndividualSelected()
    {
        var cut = RenderComponent<TfmViewToggle>(parameters => parameters
            .Add(p => p.ViewMode, TfmViewMode.Individual));

        var buttons = cut.FindAll("button");
        buttons[0].ClassList.Should().NotContain("is-primary");
        buttons[1].ClassList.Should().Contain("is-primary");
    }

    [Fact]
    public void ClickingIndividual_FiresViewModeChanged()
    {
        TfmViewMode? receivedMode = null;

        var cut = RenderComponent<TfmViewToggle>(parameters => parameters
            .Add(p => p.ViewMode, TfmViewMode.Family)
            .Add(p => p.ViewModeChanged, EventCallback.Factory.Create<TfmViewMode>(this, mode => receivedMode = mode)));

        // Click "Individual TFMs" button (second button)
        cut.FindAll("button")[1].Click();

        receivedMode.Should().Be(TfmViewMode.Individual);
    }

    [Fact]
    public void ClickingSameMode_DoesNotFireEvent()
    {
        var eventFired = false;

        var cut = RenderComponent<TfmViewToggle>(parameters => parameters
            .Add(p => p.ViewMode, TfmViewMode.Family)
            .Add(p => p.ViewModeChanged, EventCallback.Factory.Create<TfmViewMode>(this, _ => eventFired = true)));

        // Click the already-active "By Family" button
        cut.FindAll("button")[0].Click();

        eventFired.Should().BeFalse();
    }
}
