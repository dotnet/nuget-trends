using System.Security.Cryptography;
using System.Text;

// Compute a unique instance suffix from the repo directory path so multiple clones
// can run simultaneously without volume or port conflicts.
var repoDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(repoDir));
var instanceId = Convert.ToHexString(hashBytes).ToLowerInvariant()[..8];

// Derive deterministic unique ports for the Aspire dashboard from the hash.
// Aspire's dashboard Kestrel does not support dynamic port 0, so we compute
// stable per-instance ports in the ephemeral range (15000-19999).
var portOffset = (int)(BitConverter.ToUInt32(hashBytes, 0) % 5000);
var dashboardPort = 15000 + portOffset;
var otlpPort = 20000 + portOffset;
var resourcePort = 25000 + portOffset;

Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://localhost:{dashboardPort}");
Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL", $"http://localhost:{otlpPort}");
Environment.SetEnvironmentVariable("DOTNET_RESOURCE_SERVICE_ENDPOINT_URL", $"http://localhost:{resourcePort}");

var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure services
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume($"nugettrends-pgdata-{instanceId}")
    .WithPgAdmin();

var postgresDb = postgres.AddDatabase("nugettrends");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithDataVolume($"nugettrends-rabbitmq-{instanceId}")
    .WithManagementPlugin();

// ClickHouse - custom container (no official Aspire component)
// Host ports are omitted so Aspire assigns random available ports per instance.
var clickhouse = builder.AddContainer("clickhouse", "clickhouse/clickhouse-server", "25.11.5")
    .WithEnvironment("CLICKHOUSE_DB", "nugettrends")
    .WithEnvironment("CLICKHOUSE_USER", "default")
    .WithEnvironment("CLICKHOUSE_PASSWORD", "")
    .WithEnvironment("CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT", "1")
    .WithEnvironment("ALLOW_EMPTY_PASSWORD", "yes")
    .WithHttpEndpoint(targetPort: 8123, name: "http")
    .WithEndpoint(targetPort: 9000, name: "native")
    .WithBindMount("../NuGetTrends.Data/ClickHouse/migrations", "/docker-entrypoint-initdb.d", isReadOnly: true)
    .WithVolume($"nugettrends-clickhouse-{instanceId}", "/var/lib/clickhouse");

// Application services
// ClickHouse endpoint URL for injection into services.
// Aspire's WithReference on custom container endpoints sets services__* env vars,
// but our code reads ConnectionStrings__clickhouse. Inject it explicitly.
var clickhouseEndpoint = clickhouse.GetEndpoint("http");

var web = builder.AddProject<Projects.NuGetTrends_Web>("web")
    .WithReference(postgresDb)
    .WithEnvironment("ConnectionStrings__clickhouse", clickhouseEndpoint)
    .WaitFor(postgresDb)
    .WaitFor(clickhouse)
    .WithExternalHttpEndpoints();

var scheduler = builder.AddProject<Projects.NuGetTrends_Scheduler>("scheduler")
    .WithReference(postgresDb)
    .WithReference(rabbitmq)
    .WithEnvironment("ConnectionStrings__clickhouse", clickhouseEndpoint)
    .WaitFor(postgresDb)
    .WaitFor(rabbitmq)
    .WaitFor(clickhouse)
    .WithExternalHttpEndpoints();

builder.Build().Run();
