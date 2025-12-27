namespace NuGetTrends.Data.ClickHouse;

public class ClickHouseOptions
{
    public const string SectionName = "ClickHouse";

    public string? ConnectionString { get; set; }
}
