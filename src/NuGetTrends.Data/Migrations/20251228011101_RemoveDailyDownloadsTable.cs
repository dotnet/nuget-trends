using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuGetTrends.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDailyDownloadsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the view first since it depends on the table
            migrationBuilder.Sql("DROP VIEW IF EXISTS pending_packages_daily_downloads;");

            migrationBuilder.DropTable(
                name: "daily_downloads");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_downloads",
                columns: table => new
                {
                    package_id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    download_count = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_downloads", x => new { x.package_id, x.date });
                });

            // Recreate the view
            migrationBuilder.Sql(@"CREATE VIEW pending_packages_daily_downloads AS
SELECT l.package_id FROM PACKAGE_DETAILS_CATALOG_LEAFS l
LEFT OUTER JOIN DAILY_DOWNLOADS d
ON l.package_id = d.package_id AND d.date = DATE_TRUNC('day', now())
WHERE d.package_id IS NULL
GROUP BY l.package_id");
        }
    }
}
