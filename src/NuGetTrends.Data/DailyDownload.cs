using System;

namespace NuGetTrends.Data
{
    public class DailyDownload
    {
        public string? PackageId { get; set; }
        public DateTime Date { get; set; }
        public long? DownloadCount { get; set; }
    }
}
