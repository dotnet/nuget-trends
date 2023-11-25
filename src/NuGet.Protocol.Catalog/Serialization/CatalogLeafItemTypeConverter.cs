// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;
using NuGet.Protocol.Catalog.Models;

namespace NuGet.Protocol.Catalog.Serialization;

internal class CatalogLeafItemTypeConverter() : BaseCatalogLeafConverter(FromType)
{
    private static readonly Dictionary<CatalogLeafType, string> FromType = new()
    {
        { CatalogLeafType.PackageDelete, "nuget:PackageDelete" },
        { CatalogLeafType.PackageDetails, "nuget:PackageDetails" },
    };

    private static readonly Dictionary<string, CatalogLeafType> FromString = FromType
        .ToDictionary(x => x.Value, x => x.Key);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.Value is string stringValue && FromString.TryGetValue(stringValue, out var output))
        {
            return output;
        }

        throw new JsonSerializationException($"Unexpected value for a {nameof(CatalogLeafType)}.");
    }
}
