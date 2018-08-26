using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace NuGetTrends.Data.Migrations
{
    public partial class DailyDownload : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "package_registrations");

            migrationBuilder.CreateTable(
                name: "daily_download_records",
                columns: table => new
                {
                    package_id = table.Column<string>(nullable: false),
                    date = table.Column<DateTime>(nullable: false),
                    download_count = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_download_records", x => new { x.package_id, x.date });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_download_records");

            migrationBuilder.CreateTable(
                name: "package_registrations",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    package_id = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_registrations", x => x.id);
                });
        }
    }
}
