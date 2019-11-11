// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace NuGet.Protocol.Catalog.Serialization
{
    internal static class CatalogJsonSerialization
    {
        public static JsonSerializer Serializer => JsonSerializer.Create(Settings);

        private static JsonSerializerSettings Settings => new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.DateTimeOffset,
            NullValueHandling = NullValueHandling.Ignore,
        };
    }
}
