using Microsoft.EntityFrameworkCore.Migrations;

namespace NuGetTrends.Data.Migrations
{
    public partial class PendingDailyDownload : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "download_count",
                table: "daily_downloads",
                nullable: true,
                oldClrType: typeof(int),
                oldNullable: true);

            migrationBuilder.Sql(@"CREATE VIEW pending_packages_daily_downloads AS
SELECT l.package_id FROM PACKAGE_DETAILS_CATALOG_LEAFS l
LEFT OUTER JOIN DAILY_DOWNLOADS d
ON l.package_id = d.package_id AND d.date = DATE_TRUNC('day', now())
WHERE d.package_id IS NULL
GROUP BY l.package_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP VIEW pending_packages_daily_downloads");

            migrationBuilder.AlterColumn<int>(
                name: "download_count",
                table: "daily_downloads",
                nullable: true,
                oldClrType: typeof(long),
                oldNullable: true);
        }
    }
}
