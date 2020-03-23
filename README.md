<p align="center">
  <a href="https://nugettrends.com" target="_blank" align="center">
    <img src=".github/nuget-trends-full-logo.png" width="250">
  </a>
  <br />
</p>

# NuGet Trends [![Gitter chat](https://img.shields.io/gitter/room/NuGetTrends/Lobby.svg)](https://gitter.im/NuGetTrends/Lobby) [![Twitter Follow](https://img.shields.io/twitter/follow/NuGetTrends?label=NuGetTrends&style=social)](https://twitter.com/intent/follow?screen_name=NuGetTrends)

![](https://github.com/dotnet/nuget-trends/workflows/API/badge.svg) 
![](https://github.com/dotnet/nuget-trends/workflows/Scheduler/badge.svg)
![](https://github.com/dotnet/nuget-trends/workflows/SPA/badge.svg)
![Codecov](https://img.shields.io/codecov/c/github/dotnet/nuget-trends?label=SPA%20-%20Coverage)


## Summary

NuGet Trends holds historical data of NuGet packages download numbers.
It's a useful tool for package maintainers to see the download rate of their packages and also for people interested in packages popularity over time.
The database has the complete [nuget.org](https://www.nuget.org/) catalog which include target framework information.
That means that there's a lot more features we can add, like TFM adoption overtime, dependency graphs etc.

## Developing

We want to get a script to bootstrap the development environment but it's not here yet.
That means now you'll need to:

1. Run `docker-compose up` to get the services.

And either:

2. Restore a DB backup to get some data, like the packages and some download number.

Or:

2. Run _Entity Framework_ migrations to get the database schema in place.
3. Run the _catalog importer_ from scratch.
4. Run the _daily download importer_ for a few days to get some data in.

### Run the jobs

Background jobs are done using [_Hangfire_](https://github.com/HangfireIO/Hangfire). It lives in the
_NuGetTrends.Scheduler_ project. Once you run it (i.e: `dotnet run`) its dashboard will be made available through [http://localhost:5003/](http://localhost:5003/).

The jobs are scheduled to run at an interval. You can browse the dashboard and trigger the jobs on demand though.

One of the jobs is to download the [NuGet's catalog](https://docs.microsoft.com/en-us/nuget/api/catalog-resource).
The second is to hit the nuget.org's API and get the current number of downloads for each package and store in the database.

### Website

The website is composed by two parts, the single page application and the REST web API.

The SPA can be build using `yarn` and it's under `src\NuGetTrends.Spa`

The API can be run with `dotnet run` and it's under `src\NuGetTrends.Api`

### Database backup for contributors

We host a DB backup with the latest month's download counts. This will help you see some data on and fill the charts when developing.
The backup can be grabbed here: [https://contrib.nugettrends.com/nuget-trends-contrib.dump](https://contrib.nugettrends.com/nuget-trends-contrib.dump) and is a compressed _postgres_ database.

You can pull the repo and run `docker-compose up` at the root of the project to get the required services.
That will give you an empty _postgres_ database though.

### Note to .NET SDK version and global.json

We lock the .NET SDK version via `global.json` to have a reference version and avoid surprises during CI.
If you don't have that exact version, usually anything with that major works just fine.
If you just want to quickly build, try just deleting `global.json`.

<h2>.NET Foundation
<a href="https://dotnetfoundation.org/" target="_blank" align="center;bottom">
<img src=".github/dotnetfoundationhorizontal.svg" width="70">
</a>
</h2>

This project is supported by the [.NET Foundation](https://dotnetfoundation.org).

## Acknowledgments

* [Sentry.io](https://sentry.io) for supporting open source projects like this free of charge.
* [Logz.io](https://logz.io) for offering a free tier.

## Code of Conduct

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behavior in our community.
For more information see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

## Links

*  Project [announcement on reddit](https://www.reddit.com/r/dotnet/comments/ce0ffd/nugettrends_new_resource_for_net_library_authors/).
