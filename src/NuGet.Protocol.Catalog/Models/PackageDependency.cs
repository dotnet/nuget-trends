// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace NuGet.Protocol.Catalog.Models
{
    public class PackageDependency
    {
        public int Id { get; set; }

        [JsonProperty("id")]
        public string? DependencyId { get; set; }

        [JsonProperty("range")]
        public string? Range { get; set; }
    }
}
