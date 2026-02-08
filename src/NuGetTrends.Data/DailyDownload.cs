namespace NuGetTrends.Data;

public class DailyDownload
{
    public string? PackageId { get; set; }
    public DateTime Date { get; set; }
    
    /// <summary>
    /// Total download count for this package on this date.
    /// Uses long (Int64) to support values beyond int.MaxValue (2.147 billion).
    /// </summary>
    public long? DownloadCount { get; set; }
}
