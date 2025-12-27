// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;
using NuGet.Protocol.Catalog.Serialization;

namespace NuGet.Protocol.Catalog.Models;

public class PackageDependency
{
    public int Id { get; set; }

    [JsonProperty("id")]
    public string? DependencyId { get; set; }

    /// <summary>
    /// The version range for this dependency. Can be a string or an array of strings in the NuGet catalog JSON.
    /// </summary>
    [JsonProperty("range")]
    [JsonConverter(typeof(StringOrArrayConverter))]
    public string? Range { get; set; }
}