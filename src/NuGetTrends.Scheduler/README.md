# NuGetTrends.Scheduler

The project that pulls data from the NuGet API.

## EF Migrations

The EF Migration commands must be executed from the Scheduler project.

> The scheduler project is the entry point for EF Migrations (it has a [IDesignTimeDbContextFactory](https://docs.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.design.idesigntimedbcontextfactory-1?view=efcore-3.1)). The migration files are inside the `NuGetTrends.Data` project. This is mainly so things are better organized and the Data project is simply a class lib and not an executable.

Below you can find a few `dotnet ef` migration commands:

#### 1. Adding a new migration

```
/nuget-trends/src/NuGetTrends.Scheduler 
$ dotnet ef migrations add MyMigration -p ../NuGetTrends.Data
```

#### 2. Remove the latest unapplied migration

```
/nuget-trends/src/NuGetTrends.Scheduler 
$ dotnet ef migrations remove -p ../NuGetTrends.Data
```
>This will remove the latest migration file plus "rollback" the `ModelSnapshot.cs` file. It's the best way to "undo" a migration that hasn't been applied to the db yet.

#### 3. Generate a SQL script for a migration (to update the db manually)

```
/nuget-trends/src/NuGetTrends.Scheduler 
$ dotnet ef migrations script <from migration> <to migration> -o migration.sql -p ../NuGetTrends.Data
```

>Tip: You just need the class name of the migration `.cs` file. E.g `ShortenConstrNames`

>Tip: In case you want a rollback script use the same command above but invert the from/to migrations. EF Core will generate rollback script based on the `Down` methods.

#### 4. Update the database

```
/nuget-trends/src/NuGetTrends.Scheduler 
$ dotnet ef database update -p ../NuGetTrends.Data
```

> Use `-v` (verbose) to see the commands being executed

## Requirements

- EF Core global tool 5.0. See [here](https://docs.microsoft.com/en-us/ef/core/miscellaneous/cli/dotnet#installing-the-tools) for installation instructions
- Docker (to be able to start the Postgres via `docker-compose`)
