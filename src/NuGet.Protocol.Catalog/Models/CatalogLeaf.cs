// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;
using NuGet.Protocol.Catalog.Serialization;

namespace NuGet.Protocol.Catalog.Models
{
    public class CatalogLeaf : ICatalogLeafItem
    {
        [JsonProperty("@type")]
        [JsonConverter(typeof(CatalogLeafTypeConverter))]
        public CatalogLeafType Type { get; set; }

        [JsonProperty("catalog:commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        [JsonProperty("id")]
        public string? PackageId { get; set; }

        [JsonProperty("published")]
        public DateTimeOffset Published { get; set; }

        [JsonProperty("version")]
        public string? PackageVersion { get; set; }
    }
}
