using System.Text.Json.Serialization;
using NuGetTrends.Web.Client.Models;

namespace NuGetTrends.Web.Client;

[JsonSerializable(typeof(List<PackageSearchResult>))]
[JsonSerializable(typeof(PackageDownloadHistory))]
[JsonSerializable(typeof(List<TrendingPackage>))]
[JsonSerializable(typeof(List<TfmFamilyGroup>))]
[JsonSerializable(typeof(TfmAdoptionResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class NuGetTrendsJsonContext : JsonSerializerContext;
