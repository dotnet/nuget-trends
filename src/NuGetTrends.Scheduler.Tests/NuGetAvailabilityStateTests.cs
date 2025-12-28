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
            CooldownPeriod = TimeSpan.FromTicks(1) // Effectively zero
        };

        state.MarkUnavailable();

        // Cooldown already expired
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

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => state.MarkUnavailable()));
        }

        await Task.WhenAll(tasks);

        state.IsAvailable.Should().BeFalse();
        state.UnavailableSince.Should().NotBeNull();
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentMarkAvailableAndUnavailable()
    {
        var state = new NuGetAvailabilityState();
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() => state.MarkUnavailable()));
            tasks.Add(Task.Run(() => state.MarkAvailable()));
        }

        await Task.WhenAll(tasks);

        // Just ensure no exceptions - state can be either available or unavailable
        _ = state.IsAvailable;
    }

    [Fact]
    public void MarkUnavailable_WithException_SetsState()
    {
        var state = new NuGetAvailabilityState();
        var exception = new HttpRequestException("Connection reset by peer");

        state.MarkUnavailable(exception);

        state.IsAvailable.Should().BeFalse();
        state.UnavailableSince.Should().NotBeNull();
    }
}
