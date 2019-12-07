// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;
using NuGet.Protocol.Catalog.Serialization;

namespace NuGet.Protocol.Catalog.Models
{
    public class CatalogLeafItem : ICatalogLeafItem
    {
        [JsonProperty("@id")] public string Url { get; set; } = "";

        [JsonProperty("@type")]
        [JsonConverter(typeof(CatalogLeafItemTypeConverter))]
        public CatalogLeafType Type { get; set; }

        [JsonProperty("commitTimeStamp")]
        public DateTimeOffset CommitTimestamp { get; set; }

        [JsonProperty("nuget:id")]
        public string? PackageId { get; set; }

        [JsonProperty("nuget:version")]
        public string? PackageVersion { get; set; }
    }
}
