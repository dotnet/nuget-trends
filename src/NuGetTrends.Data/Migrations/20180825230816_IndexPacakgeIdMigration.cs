using Microsoft.EntityFrameworkCore.Migrations;

namespace NuGetTrends.Data.Migrations
{
    public partial class IndexPacakgeIdMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_package_details_catalog_leafs_package_id",
                table: "package_details_catalog_leafs",
                column: "package_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_package_details_catalog_leafs_package_id",
                table: "package_details_catalog_leafs");
        }
    }
}
