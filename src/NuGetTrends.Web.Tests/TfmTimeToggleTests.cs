using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NuGetTrends.Web.Client.Models;
using NuGetTrends.Web.Client.Shared;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class TfmTimeToggleTests : TestContext
{
    [Fact]
    public void Renders_TwoButtons()
    {
        var cut = RenderComponent<TfmTimeToggle>();
        var buttons = cut.FindAll("button");
        buttons.Should().HaveCount(2);
    }

    [Fact]
    public void AbsoluteButton_HasActiveClass_WhenAbsoluteSelected()
    {
        var cut = RenderComponent<TfmTimeToggle>(parameters => parameters
            .Add(p => p.TimeMode, TfmTimeMode.Absolute));

        var buttons = cut.FindAll("button");
        buttons[0].ClassList.Should().Contain("is-primary");
        buttons[1].ClassList.Should().NotContain("is-primary");
    }

    [Fact]
    public void ClickingRelative_FiresTimeModeChanged()
    {
        TfmTimeMode? receivedMode = null;

        var cut = RenderComponent<TfmTimeToggle>(parameters => parameters
            .Add(p => p.TimeMode, TfmTimeMode.Absolute)
            .Add(p => p.TimeModeChanged, EventCallback.Factory.Create<TfmTimeMode>(this, mode => receivedMode = mode)));

        // Click "Relative" button (second button)
        cut.FindAll("button")[1].Click();

        receivedMode.Should().Be(TfmTimeMode.Relative);
    }

    [Fact]
    public void ClickingSameMode_DoesNotFireEvent()
    {
        var eventFired = false;

        var cut = RenderComponent<TfmTimeToggle>(parameters => parameters
            .Add(p => p.TimeMode, TfmTimeMode.Absolute)
            .Add(p => p.TimeModeChanged, EventCallback.Factory.Create<TfmTimeMode>(this, _ => eventFired = true)));

        // Click the already-active "Calendar Dates" button
        cut.FindAll("button")[0].Click();

        eventFired.Should().BeFalse();
    }
}
