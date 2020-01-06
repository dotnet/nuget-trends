// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NuGet.Protocol.Catalog.Models;

namespace NuGet.Protocol.Catalog.Serialization
{
    internal abstract class BaseCatalogLeafConverter : JsonConverter
    {
        private readonly IReadOnlyDictionary<CatalogLeafType, string> _fromType;

        protected BaseCatalogLeafConverter(IReadOnlyDictionary<CatalogLeafType, string> fromType) => _fromType = fromType;

        public override bool CanConvert(Type objectType) => objectType == typeof(CatalogLeafType);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is {} && _fromType.TryGetValue((CatalogLeafType)value, out var output))
            {
                writer.WriteValue(output);
            }

            throw new NotSupportedException($"The catalog leaf type '{value}' is not supported.");
        }
    }
}
