using Microsoft.EntityFrameworkCore.Migrations;

namespace NuGetTrends.Data.Migrations
{
    public partial class CanscadeDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_package_dependency_package_dependency_group_package_dependenc~",
                table: "package_dependency");

            migrationBuilder.DropForeignKey(
                name: "fk_package_dependency_group_package_details_catalog_leafs_package~",
                table: "package_dependency_group");

            migrationBuilder.DropIndex(
                name: "IX_package_details_catalog_leafs_package_id_package_version",
                table: "package_details_catalog_leafs");

            migrationBuilder.CreateIndex(
                name: "IX_package_details_catalog_leafs_package_id_package_version",
                table: "package_details_catalog_leafs",
                columns: new[] { "package_id", "package_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_package_dependency_dependency_id",
                table: "package_dependency",
                column: "dependency_id");

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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_package_dependency_package_dependency_group_package_dependenc~",
                table: "package_dependency");

            migrationBuilder.DropForeignKey(
                name: "fk_package_dependency_group_package_details_catalog_leafs_package~",
                table: "package_dependency_group");

            migrationBuilder.DropIndex(
                name: "IX_package_details_catalog_leafs_package_id_package_version",
                table: "package_details_catalog_leafs");

            migrationBuilder.DropIndex(
                name: "IX_package_dependency_dependency_id",
                table: "package_dependency");

            migrationBuilder.CreateIndex(
                name: "IX_package_details_catalog_leafs_package_id_package_version",
                table: "package_details_catalog_leafs",
                columns: new[] { "package_id", "package_version" });

            migrationBuilder.AddForeignKey(
                name: "fk_package_dependency_package_dependency_group_package_dependenc~",
                table: "package_dependency",
                column: "package_dependency_group_id",
                principalTable: "package_dependency_group",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_package_dependency_group_package_details_catalog_leafs_package~",
                table: "package_dependency_group",
                column: "package_details_catalog_leaf_id",
                principalTable: "package_details_catalog_leafs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
