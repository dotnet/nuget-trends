using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace NuGetTrends.Data
{
    // ReSharper disable once ClassNeverInstantiated.Global - EF
    public class DailyDownloadResult
    {
        [Column("download_count")]
        public long? Count { get; set; }

        [Column("week")]
        public DateTime Week { get; set; }
    }
}
