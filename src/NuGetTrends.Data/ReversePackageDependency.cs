namespace NuGetTrends.Data
{
    public class ReversePackageDependency
    {
        public string PackageId { get; set; } = null!;
        public string PackageVersion { get; set; } = null!;
        public string TargetFramework { get; set; } = ""; // Old NuGet packages didn't specify TF. Empty string refers to that.
        public string DependencyPackageIdLowered { get; set; } = null!;
        public string DependencyRange { get; set; } = null!;
    }
}
