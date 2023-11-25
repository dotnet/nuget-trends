using System;
using Microsoft.Extensions.Configuration;

namespace NuGetTrends.Data;

public static class ConfigurationExtension
{
    public static string GetNuGetTrendsConnectionString(this IConfiguration configuration)
    {
        var connString = configuration.GetConnectionString("NuGetTrends");
        if (string.IsNullOrWhiteSpace(connString))
        {
            throw new InvalidOperationException("No connection string available for NuGetTrends");
        }

        return connString;
    }
}