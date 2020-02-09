using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace NuGetTrends.Data.Migrations
{
    public partial class nullablereftypes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_package_dependency_package_dependency_group_package_dependenc~",
                table: "package_dependency");

            migrationBuilder.DropForeignKey(
                name: "fk_package_dependency_group_package_details_catalog_leafs_package~",
                table: "package_dependency_group");

            migrationBuilder.AlterColumn<List<string>>(
                name: "tags",
                table: "package_details_catalog_leafs",
                nullable: false,
                oldClrType: typeof(List<string>),
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "is_prerelease",
                table: "package_details_catalog_leafs",
                nullable: true,
                oldClrType: typeof(bool));

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "package_details_catalog_leafs",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "package_dependency_group",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "package_dependency",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn);

            migrationBuilder.CreateTable(
                name: "daily_download_result",
                columns: table => new
                {
                    download_count = table.Column<long>(nullable: true),
                    week = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.AddForeignKey(
                name: "fk_package_dependency_package_dependency_group_package_dependenc",
                table: "package_dependency",
                column: "package_dependency_group_id",
                principalTable: "package_dependency_group",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_package_dependency_group_package_details_catalog_leafs_package",
                table: "package_dependency_group",
                column: "package_details_catalog_leaf_id",
                principalTable: "package_details_catalog_leafs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_package_dependency_package_dependency_group_package_dependenc",
                table: "package_dependency");

            migrationBuilder.DropForeignKey(
                name: "fk_package_dependency_group_package_details_catalog_leafs_package",
                table: "package_dependency_group");

            migrationBuilder.DropTable(
                name: "daily_download_result");

            migrationBuilder.AlterColumn<List<string>>(
                name: "tags",
                table: "package_details_catalog_leafs",
                nullable: true,
                oldClrType: typeof(List<string>));

            migrationBuilder.AlterColumn<bool>(
                name: "is_prerelease",
                table: "package_details_catalog_leafs",
                nullable: false,
                oldClrType: typeof(bool),
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "package_details_catalog_leafs",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "package_dependency_group",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "id",
                table: "package_dependency",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddForeignKey(
                name: "fk_package_dependency_package_dependency_group_package_dependenc~",
                table: "package_dependency",
                column: "package_dependency_group_id",
                principalTable: "package_dependency_group",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_package_dependency_group_package_details_catalog_leafs_package~",
                table: "package_dependency_group",
                column: "package_details_catalog_leaf_id",
                principalTable: "package_details_catalog_leafs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
