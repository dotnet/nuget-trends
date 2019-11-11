using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
// ReSharper disable RedundantArgumentDefaultValue

namespace NuGetTrends.Data.Migrations
{
    public partial class Init : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cursors",
                columns: table => new
                {
                    id = table.Column<string>(nullable: false),
                    value = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cursors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "daily_downloads",
                columns: table => new
                {
                    package_id = table.Column<string>(nullable: false),
                    date = table.Column<DateTime>(nullable: false),
                    download_count = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_downloads", x => new { x.package_id, x.date });
                });

            migrationBuilder.CreateTable(
                name: "package_details_catalog_leafs",
                columns: table => new
                {
                    type = table.Column<int>(nullable: false),
                    commit_timestamp = table.Column<DateTimeOffset>(nullable: false),
                    package_id = table.Column<string>(nullable: true),
                    published = table.Column<DateTimeOffset>(nullable: false),
                    package_version = table.Column<string>(nullable: true),
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    authors = table.Column<string>(nullable: true),
                    created = table.Column<DateTimeOffset>(nullable: false),
                    last_edited = table.Column<DateTimeOffset>(nullable: false),
                    description = table.Column<string>(nullable: true),
                    icon_url = table.Column<string>(nullable: true),
                    is_prerelease = table.Column<bool>(nullable: false),
                    language = table.Column<string>(nullable: true),
                    license_url = table.Column<string>(nullable: true),
                    listed = table.Column<bool>(nullable: true),
                    min_client_version = table.Column<string>(nullable: true),
                    package_hash = table.Column<string>(nullable: true),
                    package_hash_algorithm = table.Column<string>(nullable: true),
                    package_size = table.Column<long>(nullable: false),
                    project_url = table.Column<string>(nullable: true),
                    release_notes = table.Column<string>(nullable: true),
                    require_license_agreement = table.Column<bool>(nullable: true),
                    summary = table.Column<string>(nullable: true),
                    tags = table.Column<List<string>>(nullable: true),
                    title = table.Column<string>(nullable: true),
                    verbatim_version = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_details_catalog_leafs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "package_downloads",
                columns: table => new
                {
                    package_id = table.Column<string>(nullable: false),
                    package_id_lowered = table.Column<string>(nullable: false),
                    latest_download_count = table.Column<long>(nullable: true),
                    latest_download_count_checked_utc = table.Column<DateTime>(nullable: false),
                    icon_url = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_package_downloads", x => x.package_id);
                });

            migrationBuilder.CreateTable(
                name: "package_dependency_group",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    target_framework = table.Column<string>(nullable: true),
                    package_details_catalog_leaf_id = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_dependency_group", x => x.id);
                    table.ForeignKey(
                        name: "fk_package_dependency_group_package_details_catalog_leafs_package~",
                        column: x => x.package_details_catalog_leaf_id,
                        principalTable: "package_details_catalog_leafs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "package_dependency",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    dependency_id = table.Column<string>(nullable: true),
                    range = table.Column<string>(nullable: true),
                    package_dependency_group_id = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_dependency", x => x.id);
                    table.ForeignKey(
                        name: "fk_package_dependency_package_dependency_group_package_dependenc~",
                        column: x => x.package_dependency_group_id,
                        principalTable: "package_dependency_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"CREATE VIEW pending_packages_daily_downloads AS
SELECT l.package_id FROM PACKAGE_DETAILS_CATALOG_LEAFS l
LEFT OUTER JOIN DAILY_DOWNLOADS d
ON l.package_id = d.package_id AND d.date = DATE_TRUNC('day', now())
WHERE d.package_id IS NULL
GROUP BY l.package_id");

            migrationBuilder.InsertData(
                table: "cursors",
                columns: new[] { "id", "value" },
                values: new object[] { "Catalog", new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "IX_package_dependency_dependency_id",
                table: "package_dependency",
                column: "dependency_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_dependency_package_dependency_group_id",
                table: "package_dependency",
                column: "package_dependency_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_dependency_group_package_details_catalog_leaf_id",
                table: "package_dependency_group",
                column: "package_details_catalog_leaf_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_details_catalog_leafs_package_id",
                table: "package_details_catalog_leafs",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "IX_package_details_catalog_leafs_package_id_package_version",
                table: "package_details_catalog_leafs",
                columns: new[] { "package_id", "package_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_package_downloads_package_id_lowered",
                table: "package_downloads",
                column: "package_id_lowered",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP VIEW pending_packages_daily_downloads");

            migrationBuilder.DropTable(
                name: "cursors");

            migrationBuilder.DropTable(
                name: "daily_downloads");

            migrationBuilder.DropTable(
                name: "package_dependency");

            migrationBuilder.DropTable(
                name: "package_downloads");

            migrationBuilder.DropTable(
                name: "package_dependency_group");

            migrationBuilder.DropTable(
                name: "package_details_catalog_leafs");
        }
    }
}
