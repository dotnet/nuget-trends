// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace NuGet.Protocol.Catalog.Models;

public interface ICatalogLeafItem
{
    DateTimeOffset CommitTimestamp { get; }
    string? PackageId { get; }
    string? PackageVersion { get; }
    CatalogLeafType Type { get; }
}