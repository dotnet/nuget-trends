using FluentAssertions;
using Xunit;

namespace NuGetTrends.Scheduler.Tests;

public class NuGetAvailabilityStateTests
{
    [Fact]
    public void IsAvailable_DefaultsToTrue()
    {
        var state = new NuGetAvailabilityState();

        state.IsAvailable.Should().BeTrue();
        state.UnavailableSince.Should().BeNull();
    }

    [Fact]
    public void MarkUnavailable_SetsStateToUnavailable()
    {
        var state = new NuGetAvailabilityState();

        state.MarkUnavailable();

        state.IsAvailable.Should().BeFalse();
        state.UnavailableSince.Should().NotBeNull();
        state.UnavailableSince.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MarkUnavailable_OnlyTransitionsOnce()
    {
        var state = new NuGetAvailabilityState();

        state.MarkUnavailable();
        var firstUnavailableSince = state.UnavailableSince;

        // Wait a bit and mark again
        Thread.Sleep(50);
        state.MarkUnavailable();

        // Should still have the original timestamp
        state.UnavailableSince.Should().Be(firstUnavailableSince);
    }

    [Fact]
    public void MarkAvailable_ResetsState()
    {
        var state = new NuGetAvailabilityState();
        state.MarkUnavailable();

        state.MarkAvailable();

        state.IsAvailable.Should().BeTrue();
        state.UnavailableSince.Should().BeNull();
    }

    [Fact]
    public void IsAvailable_AutoResetsAfterCooldownPeriod()
    {
        var state = new NuGetAvailabilityState
        {
            CooldownPeriod = TimeSpan.FromMilliseconds(50)
        };

        state.MarkUnavailable();
        state.IsAvailable.Should().BeFalse();

        // Wait for cooldown to expire
        Thread.Sleep(100);

        state.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void IsAvailable_DoesNotResetBeforeCooldownExpires()
    {
        var state = new NuGetAvailabilityState
        {
            CooldownPeriod = TimeSpan.FromMinutes(10)
        };

        state.MarkUnavailable();

        state.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var state = new NuGetAvailabilityState();
        state.MarkUnavailable();

        state.Reset();

        state.IsAvailable.Should().BeTrue();
        state.UnavailableSince.Should().BeNull();
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentMarkUnavailable()
    {
        var state = new NuGetAvailabilityState();
        var tasks = new List<Task>();

        // Simulate multiple threads marking unavailable simultaneously
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => state.MarkUnavailable()));
        }

        await Task.WhenAll(tasks);

        // State should be unavailable and have a valid timestamp
        state.IsAvailable.Should().BeFalse();
        state.UnavailableSince.Should().NotBeNull();
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentMarkAvailableAndUnavailable()
    {
        var state = new NuGetAvailabilityState();
        var tasks = new List<Task>();

        // Simulate multiple threads toggling state
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() => state.MarkUnavailable()));
            tasks.Add(Task.Run(() => state.MarkAvailable()));
        }

        await Task.WhenAll(tasks);

        // State should be in a valid state (either available or unavailable)
        // We just want to ensure no exceptions were thrown
        var _ = state.IsAvailable;
    }

    [Fact]
    public void MarkUnavailable_WithException_IncludesExceptionData()
    {
        var state = new NuGetAvailabilityState();
        var exception = new HttpRequestException("Connection reset by peer");

        state.MarkUnavailable(exception);

        state.IsAvailable.Should().BeFalse();
        state.UnavailableSince.Should().NotBeNull();
    }
}
