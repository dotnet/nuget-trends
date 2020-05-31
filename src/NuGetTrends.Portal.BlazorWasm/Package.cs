using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace NuGetTrends.Portal.BlazorWasm
{
    public class Package
    {
        public string PackageId { get; set; }

        public long DownloadCount { get; set; }

        public string IconUrl { get; set; }

        public Package(string packageId, long downloadCount)
        {
            PackageId = packageId;
            DownloadCount = downloadCount;
        }
    }

    public class FormExample
    {
        [Required]
        public Package SelectedPackage { get; set; }
    }
}
