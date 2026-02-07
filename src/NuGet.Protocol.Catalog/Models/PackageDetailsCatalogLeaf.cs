// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace NuGet.Protocol.Catalog.Models;

public class PackageDetailsCatalogLeaf : CatalogLeaf
{
    public int Id { get; set; }

    [JsonProperty("authors")]
    public string? Authors { get; set; }

    [JsonProperty("created")]
    public DateTimeOffset Created { get; set; }

    [JsonProperty("lastEdited")]
    public DateTimeOffset LastEdited { get; set; }

    [JsonProperty("dependencyGroups")]
    public List<PackageDependencyGroup> DependencyGroups { get; set; } = new(0);

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("iconUrl")]
    public string? IconUrl { get; set; }

    [JsonProperty("isPrerelease")]
    public bool IsPrerelease { get; set; }

    [JsonProperty("language")]
    public string? Language { get; set; }

    [JsonProperty("licenseUrl")]
    public string? LicenseUrl { get; set; }

    [JsonProperty("listed")]
    public bool? Listed { get; set; }

    [JsonProperty("minClientVersion")]
    public string? MinClientVersion { get; set; }

    [JsonProperty("packageHash")]
    public string? PackageHash { get; set; }

    [JsonProperty("packageHashAlgorithm")]
    public string? PackageHashAlgorithm { get; set; }

    [JsonProperty("packageSize")]
    public long PackageSize { get; set; }

    [JsonProperty("projectUrl")]
    public string? ProjectUrl { get; set; }

    [JsonProperty("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    [JsonProperty("requireLicenseAgreement")]
    public bool? RequireLicenseAgreement { get; set; }

    [JsonProperty("summary")]
    public string? Summary { get; set; }

    [JsonProperty("tags")]
    public List<string>? Tags { get; set; } = new(0);

    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("verbatimVersion")]
    public string? VerbatimVersion { get; set; }

    /// <summary>
    /// Lowercase version of PackageId for efficient case-insensitive joins.
    /// This is a computed/database-only property, not sourced from JSON.
    /// </summary>
    public string? PackageIdLowered { get; set; }
}