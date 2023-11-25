// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;
using NuGet.Protocol.Catalog.Models;

namespace NuGet.Protocol.Catalog.Serialization;

internal class CatalogLeafTypeConverter() : BaseCatalogLeafConverter(FromType)
{
    private static readonly Dictionary<CatalogLeafType, string> FromType = new()
    {
        { CatalogLeafType.PackageDelete, "PackageDelete" },
        { CatalogLeafType.PackageDetails, "PackageDetails" },
    };

    private static readonly Dictionary<string, CatalogLeafType> FromString = FromType
        .ToDictionary(x => x.Value, x => x.Key);

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var types = reader.TokenType == JsonToken.StartArray || reader.Value is null
            ? serializer.Deserialize<List<object>>(reader) ?? []
            : [reader.Value];

        foreach (var type in types.OfType<string>())
        {
            if (FromString.TryGetValue(type, out var output))
            {
                return output;
            }
        }

        throw new JsonSerializationException($"Unexpected value for a {nameof(CatalogLeafType)}.");
    }
}
