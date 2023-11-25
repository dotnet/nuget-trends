// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace NuGet.Protocol.Catalog;

/// <summary>
/// Settings for how <see cref="CatalogProcessor" /> should behave. Defaults to processing all catalog items on
/// <see href="https://api.nuget.org/v3/index.json"/>.
/// </summary>
public class CatalogProcessorSettings
{
    internal CatalogProcessorSettings Clone()
    {
        return new CatalogProcessorSettings
        {
            ServiceIndexUrl = ServiceIndexUrl,
            DefaultMinCommitTimestamp = DefaultMinCommitTimestamp,
            MinCommitTimestamp = MinCommitTimestamp,
            MaxCommitTimestamp = MaxCommitTimestamp,
            ExcludeRedundantLeaves = ExcludeRedundantLeaves,
        };
    }

    /// <summary>
    /// The service index to discover the catalog index URL.
    /// </summary>
    public string ServiceIndexUrl { get; set; } = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// The minimum commit timestamp to use when no cursor value has been saved.
    /// </summary>
    public DateTimeOffset? DefaultMinCommitTimestamp { get; set; } = null;

    /// <summary>
    /// The absolute minimum (exclusive) commit timestamp to process in the catalog.
    /// </summary>
    public DateTimeOffset MinCommitTimestamp { get; set; } = DateTimeOffset.MinValue;

    /// <summary>
    /// The absolute maximum (inclusive) commit timestamp to process in the catalog.
    /// </summary>
    public DateTimeOffset MaxCommitTimestamp { get; set; } = DateTimeOffset.MaxValue;

    /// <summary>
    /// If multiple catalog leaves are found in a page concerning the same package ID and version, only the latest
    /// is processed.
    /// </summary>
    public bool ExcludeRedundantLeaves { get; set; } = true;
}
