// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NuGet.Protocol.Catalog.Serialization;

/// <summary>
/// Handles JSON properties that can be either a string or an array of strings.
/// When an array is encountered, the values are joined with a comma.
/// </summary>
public class StringOrArrayConverter : JsonConverter<string?>
{
    public override string? ReadJson(JsonReader reader, Type objectType, string? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var token = JToken.Load(reader);

        return token.Type switch
        {
            JTokenType.String => token.Value<string>(),
            JTokenType.Array => string.Join(", ", token.Values<string>()),
            JTokenType.Null => null,
            _ => throw new JsonSerializationException($"Unexpected token type '{token.Type}' when parsing string or array.")
        };
    }

    public override void WriteJson(JsonWriter writer, string? value, JsonSerializer serializer)
    {
        writer.WriteValue(value);
    }
}
