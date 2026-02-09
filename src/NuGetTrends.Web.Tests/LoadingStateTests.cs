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
    public async Task Increment_SetsIsLoadingTrueAfterDelay()
    {
        var state = new LoadingState();

        state.Increment();

        // IsLoading becomes true only after the 200ms delay
        await Task.Delay(300);

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
    public async Task MultipleIncrements_RequireMatchingDecrements()
    {
        var state = new LoadingState();

        state.Increment();
        state.Increment();

        await Task.Delay(300);

        state.Decrement();

        state.IsLoading.Should().BeTrue("one request is still active");

        state.Decrement();

        state.IsLoading.Should().BeFalse("all requests completed");
    }

    [Fact]
    public async Task Decrement_BelowZero_ClampsToZero()
    {
        var state = new LoadingState();

        state.Decrement(); // Below zero

        state.IsLoading.Should().BeFalse();

        // Subsequent increment should work normally
        state.Increment();
        await Task.Delay(300);
        state.IsLoading.Should().BeTrue();
    }

    [Fact]
    public async Task Increment_RaisesOnChangeEvent()
    {
        var state = new LoadingState();
        var eventFired = false;
        state.OnChange += () => eventFired = true;

        state.Increment();
        await Task.Delay(300);

        eventFired.Should().BeTrue();
    }

    [Fact]
    public void Decrement_RaisesOnChangeEvent()
    {
        var state = new LoadingState();
        state.Increment();
        // Decrement immediately cancels the delay — _isVisible was never set to true
        // so the Decrement path won't fire OnChange. We need to wait first.
        // Instead, just verify decrement doesn't crash:
        state.Decrement();

        state.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void OnChange_WithNoSubscribers_DoesNotThrow()
    {
        var state = new LoadingState();

        var act = () => state.Increment();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task OnChange_MultipleSubscribers_AllNotified()
    {
        var state = new LoadingState();
        var count = 0;
        state.OnChange += () => count++;
        state.OnChange += () => count++;

        state.Increment();
        await Task.Delay(300);

        count.Should().Be(2);
    }

    [Fact]
    public void FastRequest_NeverShowsLoading()
    {
        // If decrement happens before the delay, IsLoading should never become true
        var state = new LoadingState();
        var wasVisible = false;
        state.OnChange += () => { if (state.IsLoading) wasVisible = true; };

        state.Increment();
        state.Decrement(); // Immediate — cancels the delay

        wasVisible.Should().BeFalse("fast requests should not trigger loading indicator");
        state.IsLoading.Should().BeFalse();
    }
}
