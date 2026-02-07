using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuGetTrends.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakePackageIdLoweredNonNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "package_id_lowered",
                table: "package_details_catalog_leafs",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "package_id_lowered",
                table: "package_details_catalog_leafs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
