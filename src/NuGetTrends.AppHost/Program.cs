var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure services
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("nugettrends-pgdata")
    .WithPgAdmin();

var postgresDb = postgres.AddDatabase("nugettrends");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithDataVolume("nugettrends-rabbitmq")
    .WithManagementPlugin();

// ClickHouse - custom container (no official Aspire component)
var clickhouse = builder.AddContainer("clickhouse", "clickhouse/clickhouse-server", "25.11.5")
    .WithEnvironment("CLICKHOUSE_DB", "nugettrends")
    .WithEnvironment("CLICKHOUSE_USER", "default")
    .WithEnvironment("CLICKHOUSE_PASSWORD", "")
    .WithEnvironment("CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT", "1")
    .WithEnvironment("ALLOW_EMPTY_PASSWORD", "yes")
    .WithHttpEndpoint(port: 8123, targetPort: 8123, name: "http")
    .WithEndpoint(port: 9000, targetPort: 9000, name: "native")
    .WithBindMount("../NuGetTrends.Data/ClickHouse/migrations", "/docker-entrypoint-initdb.d", isReadOnly: true)
    .WithVolume("nugettrends-clickhouse", "/var/lib/clickhouse");

// Angular Portal dev server
var portal = builder.AddNpmApp("portal", "../NuGetTrends.Web/Portal", "start")
    .WithHttpEndpoint(targetPort: 4200, env: "PORT")
    .WithExternalHttpEndpoints();

// Application services
var web = builder.AddProject<Projects.NuGetTrends_Web>("web")
    .WithReference(postgresDb)
    .WithReference(clickhouse.GetEndpoint("http"))
    .WaitFor(postgresDb)
    .WaitFor(clickhouse)
    .WithExternalHttpEndpoints();

var scheduler = builder.AddProject<Projects.NuGetTrends_Scheduler>("scheduler")
    .WithReference(postgresDb)
    .WithReference(rabbitmq)
    .WithReference(clickhouse.GetEndpoint("http"))
    .WaitFor(postgresDb)
    .WaitFor(rabbitmq)
    .WaitFor(clickhouse);

builder.Build().Run();
