using FluentAssertions;
using NuGetTrends.Data;
using Xunit;

namespace NuGetTrends.Scheduler.Tests;

public class PipelineHealthCheckTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    [Theory]
    [InlineData(0, 576_000)] // Healthy night
    [InlineData(1_999, 100_000)] // Above 1%, below the floor
    [InlineData(5_000, 576_000)] // Above the floor, below 1% (a few dropped batches)
    public void IsDailyDownloadIncomplete_False_WhenFewPackagesUnchecked(int uncheckedCount, int total)
    {
        PipelineHealthCheck.IsDailyDownloadIncomplete(uncheckedCount, total).Should().BeFalse();
    }

    [Theory]
    [InlineData(576_000, 576_000)] // Nothing processed (October 2026 incident)
    [InlineData(10_000, 576_000)] // Ten dropped batches
    public void IsDailyDownloadIncomplete_True_WhenRunDidNotFinish(int uncheckedCount, int total)
    {
        PipelineHealthCheck.IsDailyDownloadIncomplete(uncheckedCount, total).Should().BeTrue();
    }

    [Fact]
    public void IsDiskLow_False_WithPlentyOfSpace()
    {
        PipelineHealthCheck.IsDiskLow(new ClickHouseDiskUsage("default", 180 * GiB, 286 * GiB)).Should().BeFalse();
    }

    [Fact]
    public void IsDiskLow_True_BelowRatio()
    {
        PipelineHealthCheck.IsDiskLow(new ClickHouseDiskUsage("default", 50 * GiB, 286 * GiB)).Should().BeTrue();
    }

    [Fact]
    public void IsDiskLow_True_BelowAbsoluteFloor()
    {
        // 25% free, but under 30 GiB.
        PipelineHealthCheck.IsDiskLow(new ClickHouseDiskUsage("default", 25 * GiB, 100 * GiB)).Should().BeTrue();
    }

    [Fact]
    public void IsDiskLow_True_WhenFull()
    {
        PipelineHealthCheck.IsDiskLow(new ClickHouseDiskUsage("default", 0, 286 * GiB)).Should().BeTrue();
    }
}
