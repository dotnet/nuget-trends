namespace NuGetTrends.Data.ClickHouse;

public class ClickHouseOptions
{
    public const string SectionName = "ClickHouse";

    public string ConnectionString { get; set; } = "Host=localhost;Port=8123;Database=nugettrends";
}
