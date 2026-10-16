namespace NuGetTrends.Data;

/// <summary>
/// Space on a ClickHouse disk as reported by <c>system.disks</c>.
/// Both values already exclude the disk's <c>keep_free_space_bytes</c> reserve.
/// </summary>
public record ClickHouseDiskUsage(string Name, ulong FreeBytes, ulong TotalBytes)
{
    public double FreeRatio => TotalBytes == 0 ? 0 : (double)FreeBytes / TotalBytes;
}
