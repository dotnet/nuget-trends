// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NuGet.Protocol.Catalog.Models;
using NuGet.Protocol.Catalog.Serialization;

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
            return DeserializeUrlAsync<CatalogIndex>(indexUrl, token);
        }

        public Task<CatalogPage> GetPageAsync(string pageUrl, CancellationToken token)
        {
            return DeserializeUrlAsync<CatalogPage>(pageUrl, token);
        }

        public async Task<CatalogLeaf> GetLeafAsync(string leafUrl, CancellationToken token)
        {
            // Buffer all of the JSON so we can parse twice. Once to determine the leaf type and once to deserialize
            // the entire thing to the proper leaf type.
            _logger.LogDebug("Downloading {leafUrl} as a byte array.", leafUrl);
            var jsonBytes = await _httpClient.GetByteArrayAsync(leafUrl);
            var untypedLeaf = DeserializeBytes<CatalogLeaf>(jsonBytes);

            switch (untypedLeaf.Type)
            {
                case CatalogLeafType.PackageDetails:
                    return DeserializeBytes<PackageDetailsCatalogLeaf>(jsonBytes);
                case CatalogLeafType.PackageDelete:
                    return DeserializeBytes<PackageDeleteCatalogLeaf>(jsonBytes);
                default:
                    throw new NotSupportedException($"The catalog leaf type '{untypedLeaf.Type}' is not supported.");
            }
        }

        private async Task<CatalogLeaf> GetLeafAsync(CatalogLeafType type, string leafUrl, CancellationToken token)
        {
            switch (type)
            {
                case CatalogLeafType.PackageDetails:
                    return await GetPackageDetailsLeafAsync(leafUrl, token);
                case CatalogLeafType.PackageDelete:
                    return await GetPackageDeleteLeafAsync(leafUrl, token);
                default:
                    throw new NotSupportedException($"The catalog leaf type '{type}' is not supported.");
            }
        }

        public Task<CatalogLeaf> GetPackageDeleteLeafAsync(string leafUrl, CancellationToken token)
        {
            return GetAndValidateLeafAsync<PackageDeleteCatalogLeaf>(CatalogLeafType.PackageDelete, leafUrl, token);
        }

        public Task<CatalogLeaf> GetPackageDetailsLeafAsync(string leafUrl, CancellationToken token)
        {
            return GetAndValidateLeafAsync<PackageDetailsCatalogLeaf>(CatalogLeafType.PackageDetails, leafUrl, token);
        }

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

                if (leaf != null && leaf.Type != type)
                {
                    _logger.LogError("The leaf type found in the document does not match the expected '{type}' type.", type);
                }

                return leaf;
            }
        }

        private T DeserializeBytes<T>(byte[] jsonBytes)
        {
            using (var stream = new MemoryStream(jsonBytes))
            using (var textReader = new StreamReader(stream))
            using (var jsonReader = new JsonTextReader(textReader))
            {
                return JsonSerializer.Deserialize<T>(jsonReader);
            }
        }

        private async Task<T> DeserializeUrlAsync<T>(string documentUrl, CancellationToken token)
        {
            _logger.LogDebug("Downloading {documentUrl} as a stream.", documentUrl);

            using (var response = await _httpClient.GetAsync(documentUrl, token))
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var textReader = new StreamReader(stream))
            using (var jsonReader = new JsonTextReader(textReader))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(jsonReader);
                }
                catch (JsonReaderException e)
                {
                    _logger.LogError(new EventId(0, documentUrl), e, "Failed to deserialize.");
                    return default;
                }
            }
        }
    }
}
