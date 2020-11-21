# NuGetTrends.Data

This project (along with `NuGet.Protocol.Catalog`) contains all the EF Core related code.

## Migrations

`NuGetTrends.Data` will hold the `Migrations` folder (AKA Migrations Assembly), but ***will not be the start-up project*** where the EF commands are executed.

The `dotnet ef` commands **must be executed** from inside the `NuGetTrends.Scheduler` folder. 
The `Scheduler` project has a [IDesignTimeDbContextFactory](https://docs.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.design.idesigntimedbcontextfactory-1?view=efcore-3.1) which knows how to create instances of the `NuGetTrendsContext`. 
The goal of this is so the `NuGetTrends.Data` project remains a simple class lib.

Below you can find a few `dotnet ef` migration commands:

#### 1. Adding a new migration

```
/nuget-trends/src/NuGetTrends.Scheduler 
$ dotnet ef migrations add MyMigration -p ../NuGetTrends.Data
```

#### 2. Remove latest unapplied migration

```
/nuget-trends/src/NuGetTrends.Scheduler 
$ dotnet ef migrations remove -p ../NuGetTrends.Data
```
>This will remove the latest migration file plus "rollback" the `ModelSnapshot.cs` file. It's the best way to "undo" a migration

#### 3. Generate a SQL script for a migration (to update the db manually)

```
/nuget-trends/src/NuGetTrends.Scheduler 
$ dotnet ef migrations script <from migration> <to migration> -o migration.sql -p ../NuGetTrends.Data
```
>Tip: You just need the "name" portion of the migration file. E.g `20180929220242_Init`, you just use `Init` in the command above

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
