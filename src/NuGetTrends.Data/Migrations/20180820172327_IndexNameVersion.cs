using Microsoft.EntityFrameworkCore.Migrations;

namespace NuGetTrends.Data.Migrations
{
    public partial class IndexNameVersion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_package_details_catalog_leafs_package_id_package_version",
                table: "package_details_catalog_leafs",
                columns: new[] { "package_id", "package_version" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_package_details_catalog_leafs_package_id_package_version",
                table: "package_details_catalog_leafs");
        }
    }
}
