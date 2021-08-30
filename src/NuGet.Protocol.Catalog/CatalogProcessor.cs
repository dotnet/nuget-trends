// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog.Models;
using NuGet.Protocol.Core.Types;
using Sentry;

namespace NuGet.Protocol.Catalog
{
    public class CatalogProcessor
    {
        private const string CatalogResourceType = "Catalog/3.0.0";
        private readonly ICatalogLeafProcessor _leafProcessor;
        private readonly ICatalogClient _client;
        private readonly ICursor _cursor;
        private readonly ILogger<CatalogProcessor> _logger;
        private readonly CatalogProcessorSettings _settings;

        public CatalogProcessor(
            ICursor cursor,
            ICatalogClient client,
            ICatalogLeafProcessor leafProcessor,
            CatalogProcessorSettings settings,
            ILogger<CatalogProcessor> logger)
        {
            _leafProcessor = leafProcessor ?? throw new ArgumentNullException(nameof(leafProcessor));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings.ServiceIndexUrl == null)
            {
                throw new ArgumentException(
                    $"The {nameof(CatalogProcessorSettings.ServiceIndexUrl)} property of the " +
                    $"{nameof(CatalogProcessorSettings)} must not be null.",
                    nameof(settings));
            }

            // Clone the settings to avoid mutability issues.
            _settings = settings.Clone();
            SentrySdk.ConfigureScope(s => s.Contexts["CatalogProcessorSettings"] = _settings);
        }

        /// <summary>
        /// Discovers and downloads all of the catalog leafs after the current cursor value and before the maximum
        /// commit timestamp found in the settings. Each catalog leaf is passed to the catalog leaf processor in
        /// chronological order. After a commit is completed, its commit timestamp is written to the cursor, i.e. when
        /// transitioning from commit timestamp A to B, A is written to the cursor so that it never is processed again.
        /// </summary>
        public async Task ProcessAsync(CancellationToken token)
        {
            var catalogIndexSpan = SentrySdk.GetSpan()?.StartChild("catalog.index", "Retrieving catalog index");
            var catalogIndexUrl = await GetCatalogIndexUrlAsync(token);
            catalogIndexSpan?.SetTag("catalogIndexUrl", catalogIndexUrl);
            catalogIndexSpan?.Finish();

            var minCommitTimestamp = await GetMinCommitTimestamp(token);

            _logger.LogInformation(
                "Using time bounds {min:O} (exclusive) to {max:O} (inclusive).",
                minCommitTimestamp,
                _settings.MaxCommitTimestamp);

            var processIndexSpan = SentrySdk.GetSpan()?.StartChild("catalog.process", "Processing catalog");
            processIndexSpan?.SetTag("minCommitTimestamp", minCommitTimestamp.ToString());
            await ProcessIndexAsync(catalogIndexUrl, minCommitTimestamp, token);
            processIndexSpan?.Finish();
        }

        private async Task ProcessIndexAsync(string catalogIndexUrl, DateTimeOffset minCommitTimestamp, CancellationToken token)
        {
            var index = await _client.GetIndexAsync(catalogIndexUrl, token);

            var pageItems = index.GetPagesInBounds(
                minCommitTimestamp,
                _settings.MaxCommitTimestamp);

            _logger.LogInformation(
                "{pages} pages were in the time bounds, out of {totalPages}.",
                pageItems.Count,
                index.Items.Count);

            foreach (var pageItem in pageItems)
            {
                using (_logger.BeginScope(("page", pageItem)))
                {
                    await ProcessPageAsync(minCommitTimestamp, pageItem, token);
                }
            }
        }

        private async Task ProcessPageAsync(DateTimeOffset minCommitTimestamp, CatalogPageItem pageItem, CancellationToken token)
        {
            var page = await _client.GetPageAsync(pageItem.Url, token);

            var leafItems = page.GetLeavesInBounds(
                minCommitTimestamp,
                _settings.MaxCommitTimestamp,
                _settings.ExcludeRedundantLeaves);

            SentrySdk.GetSpan()?.SetTag("leafItemsCount", leafItems.Count.ToString());

            _logger.LogInformation(
                "On page {page}, {leaves} out of {totalLeaves} were in the time bounds.",
                pageItem.Url,
                leafItems.Count,
                page.Items.Count);

            DateTimeOffset? newCursor = null;

            var tasks = new List<Task<CatalogLeaf>>();

            foreach (var batch in leafItems
                .Select((v, i) => new { Index = i, Value = v })
                .GroupBy(v => v.Index / 25)
                .Select(v => v.Select(p => p.Value)))
            {
                foreach (var leafItem in batch)
                {
                    newCursor = leafItem.CommitTimestamp;

                    tasks.Add(ProcessLeafAsync(leafItem, token));
                }

                await Task.WhenAll(tasks);

                foreach (var task in tasks)
                {
                    try
                    {
                        if (task.Result is PackageDeleteCatalogLeaf del)
                        {
                            await _leafProcessor.ProcessPackageDeleteAsync(del, token);
                        }
                        else if (task.Result is PackageDetailsCatalogLeaf detail)
                        {
                            await _leafProcessor.ProcessPackageDetailsAsync(detail, token);
                        }
                        else
                        {
                            // Lots of null leafs
                            _logger.LogInformation("Unsupported leaf type: {type}.", task.Result?.GetType());
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to process {result}.", task.Result);
                    }
                }

                tasks.Clear();
            }

            if (newCursor.HasValue)
            {
                await _cursor.SetAsync(newCursor.Value, token);
            }
        }

        private Task<CatalogLeaf> ProcessLeafAsync(CatalogLeafItem leafItem, CancellationToken token) =>
            leafItem.Type switch
            {
                CatalogLeafType.PackageDelete => _client.GetPackageDeleteLeafAsync(leafItem.Url, token),
                CatalogLeafType.PackageDetails => _client.GetPackageDetailsLeafAsync(leafItem.Url, token),
                _ => throw new NotSupportedException($"The catalog leaf type '{leafItem.Type}' is not supported.")
            };

        private async Task<DateTimeOffset> GetMinCommitTimestamp(CancellationToken token)
        {
            var minCommitTimestamp = await _cursor.GetAsync(token);

            minCommitTimestamp ??= _settings.DefaultMinCommitTimestamp
                                   ?? _settings.MinCommitTimestamp;

            if (minCommitTimestamp.Value < _settings.MinCommitTimestamp)
            {
                minCommitTimestamp = _settings.MinCommitTimestamp;
            }

            return minCommitTimestamp.Value;
        }

        private async Task<string> GetCatalogIndexUrlAsync(CancellationToken token)
        {
            _logger.LogInformation("Getting catalog index URL from {serviceIndexUrl}.", _settings.ServiceIndexUrl);
            var sourceRepository = Repository.Factory.GetCoreV3(_settings.ServiceIndexUrl, FeedType.HttpV3);
            var serviceIndexResource = await sourceRepository.GetResourceAsync<ServiceIndexResourceV3>(token);
            var catalogIndexUrl = serviceIndexResource.GetServiceEntryUri(CatalogResourceType)?.AbsoluteUri;
            if (catalogIndexUrl == null)
            {
                throw new InvalidOperationException(
                    $"The service index does not contain resource '{CatalogResourceType}'.");
            }

            return catalogIndexUrl;
        }
    }
}
