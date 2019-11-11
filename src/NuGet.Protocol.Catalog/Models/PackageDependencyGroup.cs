// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace NuGet.Protocol.Catalog.Models
{
    public class PackageDependencyGroup
    {
        public int Id { get; set; }

        [JsonProperty("targetFramework")]
        public string? TargetFramework { get; set; }

        [JsonProperty("dependencies")]
        public List<PackageDependency> Dependencies { get; set; } = new List<PackageDependency>(0);
    }
}
