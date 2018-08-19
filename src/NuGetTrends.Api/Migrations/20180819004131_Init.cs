using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace NuGetTrends.Api.Migrations
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
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "package_dependency",
                columns: table => new
                {
                    id = table.Column<string>(nullable: false),
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
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "cursors",
                columns: new[] { "id", "value" },
                values: new object[] { "Catalog", new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "ix_package_dependency_package_dependency_group_id",
                table: "package_dependency",
                column: "package_dependency_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_package_dependency_group_package_details_catalog_leaf_id",
                table: "package_dependency_group",
                column: "package_details_catalog_leaf_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cursors");

            migrationBuilder.DropTable(
                name: "package_dependency");

            migrationBuilder.DropTable(
                name: "package_registrations");

            migrationBuilder.DropTable(
                name: "package_dependency_group");

            migrationBuilder.DropTable(
                name: "package_details_catalog_leafs");
        }
    }
}
