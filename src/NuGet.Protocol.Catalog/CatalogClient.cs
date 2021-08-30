// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NuGet.Protocol.Catalog.Models;
using NuGet.Protocol.Catalog.Serialization;
using Sentry;

namespace NuGet.Protocol.Catalog
{
    public class CatalogClient : ICatalogClient
    {
        private static readonly JsonSerializer JsonSerializer = CatalogJsonSerialization.Serializer;
        private readonly HttpClient _httpClient;
        private readonly ILogger<CatalogClient> _logger;

        public CatalogClient(HttpClient httpClient, ILogger<CatalogClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<CatalogIndex> GetIndexAsync(string indexUrl, CancellationToken token)
        {
            var index = DeserializeUrlAsync<CatalogIndex>(indexUrl, token);
            if (index is null)
            {
                throw new InvalidOperationException($"Url {indexUrl} didn't return a CatalogIndex.");
            }

            return index!;
        }

        public Task<CatalogPage> GetPageAsync(string pageUrl, CancellationToken token)
        {
            var page = DeserializeUrlAsync<CatalogPage>(pageUrl, token);
            if (page is null)
            {
                throw new InvalidOperationException($"Url {pageUrl} didn't return a CatalogPage.");
            }

            return page!;
        }

        public async Task<CatalogLeaf> GetLeafAsync(string leafUrl, CancellationToken token)
        {
            // Buffer all of the JSON so we can parse twice. Once to determine the leaf type and once to deserialize
            // the entire thing to the proper leaf type.
            _logger.LogDebug("Downloading {leafUrl} as a byte array.", leafUrl);
            var jsonBytes = await _httpClient.GetByteArrayAsync(leafUrl);
            var untypedLeaf = DeserializeBytes<CatalogLeaf>(jsonBytes);

            return untypedLeaf.Type switch
            {
                CatalogLeafType.PackageDetails => (CatalogLeaf)DeserializeBytes<PackageDetailsCatalogLeaf>(jsonBytes),
                CatalogLeafType.PackageDelete => DeserializeBytes<PackageDeleteCatalogLeaf>(jsonBytes),
                _ => throw new NotSupportedException($"The catalog leaf type '{untypedLeaf.Type}' is not supported.")
            };
        }

        public Task<CatalogLeaf> GetPackageDeleteLeafAsync(string leafUrl, CancellationToken token)
            => GetAndValidateLeafAsync<PackageDeleteCatalogLeaf>(CatalogLeafType.PackageDelete, leafUrl, token);

        public Task<CatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken token)
            => GetAndValidateLeafAsync<PackageDetailsCatalogLeaf>(CatalogLeafType.PackageDetails, leafUrl, token);

        public async Task<CatalogLeaf> GetAndValidateLeafAsync<T>(CatalogLeafType type, string leafUrl, CancellationToken token) where T : CatalogLeaf
        {
            using (_logger.BeginScope(new Dictionary<string, string>
            {
                { "type", type.ToString()},
                { "leafUrl", leafUrl},
            }))
            {
                _logger.LogInformation("Getting package leaf: {type}, {leafUrl}", type, leafUrl);
                var leaf = await DeserializeUrlAsync<T>(leafUrl, token);

                if (leaf is null)
                {
                    throw new InvalidOperationException("Leaf URL: {leafUrl} didn't return a valid leaf object.");
                }

                if (leaf.Type != type)
                {
                    _logger.LogError("The leaf type found in the document does not match the expected '{type}' type.", type);
                }

                return leaf!;
            }
        }

        private static T DeserializeBytes<T>(byte[] jsonBytes)
            where T : class
        {
            using var stream = new MemoryStream(jsonBytes);
            using var textReader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(textReader);
            var result = JsonSerializer.Deserialize<T>(jsonReader);
            if (result == null)
            {
                throw new InvalidOperationException("Deserialization resulted in null");
            }

            return result;
        }

        private async Task<T?> DeserializeUrlAsync<T>(string documentUrl, CancellationToken token)
            where T : class
        {
            _logger.LogDebug("Downloading {documentUrl} as a stream.", documentUrl);

            using var response = await _httpClient.GetAsync(documentUrl, token);
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var textReader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(textReader);
            var deserializingSpan = SentrySdk.GetSpan()
                ?.StartChild("json.deserialize", "Deserializing response: " + documentUrl);
            try
            {
                var responseOfT = JsonSerializer.Deserialize<T?>(jsonReader);
                deserializingSpan?.Finish(SpanStatus.Ok);
                return responseOfT;
            }
            catch (JsonReaderException e)
            {
                _logger.LogError(new EventId(0, documentUrl), e, "Failed to deserialize.");
                deserializingSpan?.Finish(e);
                return default!;
            }
        }
    }
}
