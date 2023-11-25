// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;
using NuGet.Protocol.Catalog.Models;

namespace NuGet.Protocol.Catalog.Serialization;

internal abstract class BaseCatalogLeafConverter(IReadOnlyDictionary<CatalogLeafType, string> fromType) : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(CatalogLeafType);

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is not null && fromType.TryGetValue((CatalogLeafType)value, out var output))
        {
            writer.WriteValue(output);
        }

        throw new NotSupportedException($"The catalog leaf type '{value}' is not supported.");
    }
}
