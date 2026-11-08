using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telumera.Services.SiteRegistry.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteArchivedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                table: "sites",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "sites");
        }
    }
}
