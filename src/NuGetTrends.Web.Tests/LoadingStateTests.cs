using FluentAssertions;
using NuGetTrends.Web.Client.Services;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class LoadingStateTests
{
    [Fact]
    public void IsLoading_Initially_ReturnsFalse()
    {
        var state = new LoadingState();

        state.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void Increment_SetsIsLoadingTrue()
    {
        var state = new LoadingState();

        state.Increment();

        state.IsLoading.Should().BeTrue();
    }

    [Fact]
    public void Decrement_AfterIncrement_SetsIsLoadingFalse()
    {
        var state = new LoadingState();
        state.Increment();

        state.Decrement();

        state.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void MultipleIncrements_RequireMatchingDecrements()
    {
        var state = new LoadingState();

        state.Increment();
        state.Increment();
        state.Decrement();

        state.IsLoading.Should().BeTrue("one request is still active");

        state.Decrement();

        state.IsLoading.Should().BeFalse("all requests completed");
    }

    [Fact]
    public void Decrement_BelowZero_ClampsToZero()
    {
        var state = new LoadingState();

        state.Decrement(); // Below zero

        state.IsLoading.Should().BeFalse();

        // Subsequent increment should work normally
        state.Increment();
        state.IsLoading.Should().BeTrue();
    }

    [Fact]
    public void Increment_RaisesOnChangeEvent()
    {
        var state = new LoadingState();
        var eventFired = false;
        state.OnChange += () => eventFired = true;

        state.Increment();

        eventFired.Should().BeTrue();
    }

    [Fact]
    public void Decrement_RaisesOnChangeEvent()
    {
        var state = new LoadingState();
        state.Increment();
        var eventFired = false;
        state.OnChange += () => eventFired = true;

        state.Decrement();

        eventFired.Should().BeTrue();
    }

    [Fact]
    public void OnChange_WithNoSubscribers_DoesNotThrow()
    {
        var state = new LoadingState();

        var act = () => state.Increment();

        act.Should().NotThrow();
    }

    [Fact]
    public void OnChange_MultipleSubscribers_AllNotified()
    {
        var state = new LoadingState();
        var count = 0;
        state.OnChange += () => count++;
        state.OnChange += () => count++;

        state.Increment();

        count.Should().Be(2);
    }
}
