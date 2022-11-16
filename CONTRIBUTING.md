# Contributing

Please raise an issue to discuss changes before raising large PRs.

### Requirements

- .NET SDK 7
- Docker, Compose (for the dependencies, postgres, rabbitmq)
- Node.js 15.0.1 (might also work with higher versions)
- Yarn (We use Yarn instead of npm)


### Run dependant services

We want to get a script to bootstrap the development environment but it's not here yet.
That means now you'll need to:

1. Run `docker-compose up` to get the services.

And either:

2. Restore a [DB backup to get some data](#database-backup-for-contributors), like the packages and some download number.

**Or:**

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

The website is composed by two parts: An Angular SPA and an ASP.NET Core API. The API is at the root of `src/NuGetTrends.Web/` and the SPA is in the `Portal` sub-folder.

To run it locally:

**SPA** (src/NuGetTrends.Web/Portal)
1. Install the SPA dependencies: `yarn install` (only the first time)
2. Run the SPA: `ng serve`

**API** (src/NuGetTrends.Web)
2. Run the API with `dotnet run`

The app can be browsed at:
- `http://localhost:5000`
- `https://localhost:5001`
- `http://localhost:4200` (default `ng serve` port)

> Note: You might need to see how to [install/trust the ASP.NET Core HTTPS development certificates](https://docs.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-5.0&tabs=visual-studio#trust-the-aspnet-core-https-development-certificate-on-windows-and-macos) (not required but nice to have)

### Database backup for contributors

We host a DB backup with the latest month's download counts. This will help you see some data on and fill the charts when developing.
The backup can be grabbed here: [https://contrib.nugettrends.com/nuget-trends-contrib.dump](https://contrib.nugettrends.com/nuget-trends-contrib.dump) and is a compressed _postgres_ database.

You can pull the repo and run `docker-compose up` at the root of the project to get the required services.
That will give you an empty _postgres_ database though.

For example, [with `pg_restore`](https://command-not-found.com/pg_restore).

```sh
# the db password hard coded in docker-compose.yml and appsettings.json config files:
export PGPASSWORD=PUg2rt6Pp8Arx7Z9FbgJLFvxEL7pZ2

# create an empty db
createdb nugettrends -U postgres -h localhost -p 5432

# restore backup
pg_restore -U postgres -d nugettrends -h localhost -p 5432 nuget-trends-contrib.dump
```

### Note to .NET SDK version and global.json

We lock the .NET SDK version via `global.json` to have a reference version and avoid surprises during CI.
If you don't have that exact version, usually anything with that major works just fine.
If you just want to quickly build, try just deleting `global.json`.

Please note we have a code of conduct, please follow it in all your interactions with the project.
