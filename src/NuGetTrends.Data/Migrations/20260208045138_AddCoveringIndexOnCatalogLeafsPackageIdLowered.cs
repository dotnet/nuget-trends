using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuGetTrends.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoveringIndexOnCatalogLeafsPackageIdLowered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_package_details_catalog_leafs_package_id_lowered",
                table: "package_details_catalog_leafs");

            migrationBuilder.CreateIndex(
                name: "IX_package_details_catalog_leafs_package_id_lowered",
                table: "package_details_catalog_leafs",
                column: "package_id_lowered")
                .Annotation("Npgsql:IndexInclude", new[] { "package_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_package_details_catalog_leafs_package_id_lowered",
                table: "package_details_catalog_leafs");

            migrationBuilder.CreateIndex(
                name: "IX_package_details_catalog_leafs_package_id_lowered",
                table: "package_details_catalog_leafs",
                column: "package_id_lowered");
        }
    }
}
