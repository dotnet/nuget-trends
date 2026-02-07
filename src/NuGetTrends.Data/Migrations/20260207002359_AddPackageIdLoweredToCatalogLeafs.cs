using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuGetTrends.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageIdLoweredToCatalogLeafs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "package_id_lowered",
                table: "package_details_catalog_leafs",
                type: "text",
                nullable: true);

            // Populate existing rows with lowercased package_id
            migrationBuilder.Sql(
                @"UPDATE package_details_catalog_leafs 
                  SET package_id_lowered = LOWER(package_id) 
                  WHERE package_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_package_details_catalog_leafs_package_id_lowered",
                table: "package_details_catalog_leafs",
                column: "package_id_lowered");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_package_details_catalog_leafs_package_id_lowered",
                table: "package_details_catalog_leafs");

            migrationBuilder.DropColumn(
                name: "package_id_lowered",
                table: "package_details_catalog_leafs");
        }
    }
}
