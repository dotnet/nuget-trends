<p align="center">
  <a href="https://nugettrends.com" target="_blank" align="center">
    <img src=".github/nuget-trends-full-logo.png" width="250">
  </a>
  <br />
</p>

# NuGet Trends [![Twitter Follow](https://img.shields.io/twitter/follow/NuGetTrends?label=NuGetTrends&style=social)](https://twitter.com/intent/follow?screen_name=NuGetTrends)

[![Nuget Trends Web](https://github.com/dotnet/nuget-trends/workflows/Web/badge.svg)](https://github.com/dotnet/nuget-trends/actions?query=workflow%3AWeb)
[![Scheduler](https://github.com/dotnet/nuget-trends/workflows/Scheduler/badge.svg)](https://github.com/dotnet/nuget-trends/actions?query=workflow%3AScheduler)

## Summary

NuGet Trends holds historical data of NuGet packages download numbers.
It's a useful tool for package maintainers to see the download rate of their packages and also for people interested in packages popularity over time.
The database has the complete [nuget.org](https://www.nuget.org/) catalog which include target framework information.
That means that there's a lot more features we can add, like TFM adoption overtime, dependency graphs etc.

## Running Locally

The project uses [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/) for local development. Start everything with:

```bash
dotnet run --project src/NuGetTrends.AppHost
```

This starts PostgreSQL, RabbitMQ, ClickHouse, the Angular SPA, Web API, and Scheduler - all with a single command.

The **Aspire Dashboard** URL is shown in the terminal output, providing a view of all services, logs, and traces.

See [CONTRIBUTING.md](CONTRIBUTING.md) for detailed setup instructions.

## Docker

Images published at: https://hub.docker.com/u/nugettrends

## Contributing

Please refer to [CONTRIBUTING.md](CONTRIBUTING.md).

## Links

*  Project [announcement on Reddit](https://www.reddit.com/r/dotnet/comments/ce0ffd/nugettrends_new_resource_for_net_library_authors/).
* .NET Docs Show 
<div align="left">
      <a href="https://www.youtube.com/watch?v=eYHpsodYLJA">
         <img src="https://img.youtube.com/vi/eYHpsodYLJA/0.jpg" style="width:100%;">
      </a>
</div>

## Sponsors

<a href="https://sentry.io" target="_blank">
  <img src=".github/sentry-logo.svg" width="200" alt="Sentry">
</a>

NuGet Trends is proudly hosted by [Sentry](https://sentry.io). They cover all infrastructure costs including servers, database, and provide their application monitoring platform for free. Thank you, Sentry, for [supporting open source](https://opensourcepledge.com/members/sentry/)!
