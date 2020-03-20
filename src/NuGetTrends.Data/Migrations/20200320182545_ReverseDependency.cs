using Microsoft.EntityFrameworkCore.Migrations;

namespace NuGetTrends.Data.Migrations
{
    public partial class ReverseDependency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "is_prerelease",
                table: "package_details_catalog_leafs",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "reverse_package_dependencies",
                columns: table => new
                {
                    package_id = table.Column<string>(nullable: false),
                    package_version = table.Column<string>(nullable: false),
                    target_framework = table.Column<string>(nullable: false),
                    dependency_package_id_lowered = table.Column<string>(nullable: false),
                    dependency_range = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reverse_package_dependencies", x => new { x.target_framework, x.package_id, x.package_version, x.dependency_package_id_lowered, x.dependency_range });
                });

            migrationBuilder.CreateIndex(
                name: "IX_reverse_package_dependencies_dependency_package_id_lowered",
                table: "reverse_package_dependencies",
                column: "dependency_package_id_lowered");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reverse_package_dependencies");

            migrationBuilder.AlterColumn<bool>(
                name: "is_prerelease",
                table: "package_details_catalog_leafs",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool));
        }
    }
}
