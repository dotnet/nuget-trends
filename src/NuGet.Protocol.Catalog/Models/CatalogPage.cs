// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace NuGet.Protocol.Catalog.Models;

public class CatalogPage
{
    [JsonProperty("commitTimeStamp")]
    public DateTimeOffset CommitTimestamp { get; set; }

    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("items")]
    public List<CatalogLeafItem> Items { get; set; } = new(0);

    [JsonProperty("parent")]
    public string? Parent { get; set; }
}