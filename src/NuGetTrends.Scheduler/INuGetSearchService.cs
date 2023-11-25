﻿using NuGet.Protocol.Core.Types;

namespace NuGetTrends.Scheduler;

public interface INuGetSearchService
{
    Task<IPackageSearchMetadata?> GetPackage(string packageId, CancellationToken token);
}
