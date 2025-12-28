using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuGetTrends.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShortLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS short_links");
            migrationBuilder.Sql("DROP TABLE IF EXISTS shortlinks");
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"ShortLinks\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally left blank. Recreating the Shortr tables is not supported.
        }
    }
}
