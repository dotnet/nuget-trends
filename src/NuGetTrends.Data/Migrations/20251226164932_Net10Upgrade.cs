using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuGetTrends.Data.Migrations
{
    /// <inheritdoc />
    public partial class Net10Upgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "latest_download_count_checked_utc",
                table: "package_downloads",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            // Note: DailyDownloadResult is a keyless entity for raw SQL queries, not a real table
            // So we skip altering it here

            // Drop the view that depends on the date column
            migrationBuilder.Sql("DROP VIEW IF EXISTS pending_packages_daily_downloads");

            migrationBuilder.AlterColumn<DateTime>(
                name: "date",
                table: "daily_downloads",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            // Recreate the view
            migrationBuilder.Sql(@"CREATE VIEW pending_packages_daily_downloads AS
SELECT l.package_id FROM PACKAGE_DETAILS_CATALOG_LEAFS l
LEFT OUTER JOIN DAILY_DOWNLOADS d
ON l.package_id = d.package_id AND d.date = DATE_TRUNC('day', now())
WHERE d.package_id IS NULL
GROUP BY l.package_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "latest_download_count_checked_utc",
                table: "package_downloads",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            // Note: DailyDownloadResult is a keyless entity for raw SQL queries, not a real table
            // So we skip altering it here

            migrationBuilder.AlterColumn<DateTime>(
                name: "date",
                table: "daily_downloads",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");
        }
    }
}
