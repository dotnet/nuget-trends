using System;

namespace NuGetTrends.Data
{
    public class DailyDownloadResult
    {
        public double? downloadcount { get; set; }

        public DateTime week { get; set; }
    }
}
