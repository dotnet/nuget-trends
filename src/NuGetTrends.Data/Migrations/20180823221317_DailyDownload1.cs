using Microsoft.EntityFrameworkCore.Migrations;

namespace NuGetTrends.Data.Migrations
{
    public partial class DailyDownload1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_daily_download_records",
                table: "daily_download_records");

            migrationBuilder.RenameTable(
                name: "daily_download_records",
                newName: "daily_downloads");

            migrationBuilder.AddPrimaryKey(
                name: "PK_daily_downloads",
                table: "daily_downloads",
                columns: new[] { "package_id", "date" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_daily_downloads",
                table: "daily_downloads");

            migrationBuilder.RenameTable(
                name: "daily_downloads",
                newName: "daily_download_records");

            migrationBuilder.AddPrimaryKey(
                name: "PK_daily_download_records",
                table: "daily_download_records",
                columns: new[] { "package_id", "date" });
        }
    }
}
