// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace NuGet.Protocol.Catalog.Models;

/// <summary>
/// These are documented interpretations of values returned by the catalog API.
/// </summary>
public static class ModelExtensions
{
    /// <summary>
    /// Gets the leaves that lie within the provided commit timestamp bounds. The result is sorted by commit
    /// timestamp, then package ID, then package version (SemVer order).
    /// </summary>
    /// <param name="catalogPage"></param>
    /// <param name="minCommitTimestamp">The exclusive lower time bound on <see cref="CatalogLeafItem.CommitTimestamp"/>.</param>
    /// <param name="maxCommitTimestamp">The inclusive upper time bound on <see cref="CatalogLeafItem.CommitTimestamp"/>.</param>
    /// <param name="excludeRedundantLeaves">Only show the latest leaf concerning each package.</param>
    public static List<CatalogLeafItem> GetLeavesInBounds(
        this CatalogPage catalogPage,
        DateTimeOffset minCommitTimestamp,
        DateTimeOffset maxCommitTimestamp,
        bool excludeRedundantLeaves)
    {
        var leaves = catalogPage
            .Items
            .Where(x => x.CommitTimestamp > minCommitTimestamp && x.CommitTimestamp <= maxCommitTimestamp)
            .OrderBy(x => x.CommitTimestamp);

        if (excludeRedundantLeaves)
        {
            leaves = leaves
                .GroupBy(x => new PackageIdentity(x.PackageId, x.ParsePackageVersion()))
                .Select(x => x.Last())
                .OrderBy(x => x.CommitTimestamp);
        }

        return leaves
            .ThenBy(x => x.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ParsePackageVersion())
            .ToList();
    }

    /// <summary>
    /// Gets the pages that may have catalog leaves within the provided commit timestamp bounds. The result is
    /// sorted by commit timestamp.
    /// </summary>
    /// <param name="catalogIndex">The catalog index to fetch pages from.</param>
    /// <param name="minCommitTimestamp">The exclusive lower time bound on <see cref="CatalogPageItem.CommitTimestamp"/>.</param>
    /// <param name="maxCommitTimestamp">The inclusive upper time bound on <see cref="CatalogPageItem.CommitTimestamp"/>.</param>
    public static List<CatalogPageItem> GetPagesInBounds(
        this CatalogIndex catalogIndex,
        DateTimeOffset minCommitTimestamp,
        DateTimeOffset maxCommitTimestamp)
    {
        return catalogIndex
            .GetPagesInBoundsLazy(minCommitTimestamp, maxCommitTimestamp)
            .ToList();
    }

    private static IEnumerable<CatalogPageItem> GetPagesInBoundsLazy(
        this CatalogIndex catalogIndex,
        DateTimeOffset minCommitTimestamp,
        DateTimeOffset maxCommitTimestamp)
    {
        // Filter out pages that fall entirely before the minimum commit timestamp and sort the remaining pages by
        // commit timestamp.
        var upperRange = catalogIndex
            .Items
            .Where(x => x.CommitTimestamp > minCommitTimestamp)
            .OrderBy(x => x.CommitTimestamp);

        // Take pages from the sorted list until the commit timestamp goes past the maximum commit timestamp. This
        // essentially LINQ's TakeWhile plus one more element.
        foreach (var page in upperRange)
        {
            yield return page;

            if (page.CommitTimestamp > maxCommitTimestamp)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Parse the package version as a <see cref="NuGetVersion" />.
    /// </summary>
    /// <param name="leaf">The catalog leaf.</param>
    /// <returns>The package version.</returns>
    private static NuGetVersion ParsePackageVersion(this ICatalogLeafItem leaf) => NuGetVersion.Parse(leaf.PackageVersion);

    /// <summary>
    /// Parse the target framework as a <see cref="NuGetFramework" />.
    /// </summary>
    /// <param name="packageDependencyGroup">The package dependency group.</param>
    /// <returns>The framework.</returns>
    public static NuGetFramework ParseTargetFramework(this PackageDependencyGroup packageDependencyGroup)
    {
        if (string.IsNullOrEmpty(packageDependencyGroup.TargetFramework))
        {
            return NuGetFramework.AnyFramework;
        }

        return NuGetFramework.Parse(packageDependencyGroup.TargetFramework);
    }

    /// <summary>
    /// Parse the version range as a <see cref="VersionRange"/>.
    /// </summary>
    /// <param name="packageDependency">The package dependency.</param>
    /// <returns>The version range.</returns>
    public static VersionRange ParseRange(this PackageDependency packageDependency)
    {
        if (string.IsNullOrEmpty(packageDependency.Range))
        {
            return VersionRange.All;
        }

        return VersionRange.Parse(packageDependency.Range);
    }

    /// <summary>
    /// Determines if the provided catalog leaf is a package delete.
    /// </summary>
    /// <param name="leaf">The catalog leaf.</param>
    /// <returns>True if the catalog leaf represents a package delete.</returns>
    public static bool IsPackageDelete(this ICatalogLeafItem leaf)
    {
        return leaf.Type == CatalogLeafType.PackageDelete;
    }

    /// <summary>
    /// Determines if the provided catalog leaf is contains package details.
    /// </summary>
    /// <param name="leaf">The catalog leaf.</param>
    /// <returns>True if the catalog leaf contains package details.</returns>
    public static bool IsPackageDetails(this ICatalogLeafItem leaf)
    {
        return leaf.Type == CatalogLeafType.PackageDetails;
    }

    /// <summary>
    /// Determines if the provided package details list represents a listed package.
    /// </summary>
    /// <param name="leaf">The catalog leaf.</param>
    /// <returns>True if the package is listed.</returns>
    public static bool IsListed(this PackageDetailsCatalogLeaf leaf)
    {
        if (leaf.Listed.HasValue)
        {
            return leaf.Listed.Value;
        }

        // A published year of 1900 indicates that this package is unlisted, when the listed property itself is
        // not present (legacy behavior).
        return leaf.Published.Year == 1900;
    }
}