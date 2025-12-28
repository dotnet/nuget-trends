using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuGetTrends.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShortenedUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS shortened_urls");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
