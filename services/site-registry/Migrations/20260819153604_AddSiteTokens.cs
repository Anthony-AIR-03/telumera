using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telumera.Services.SiteRegistry.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sites_browser_token",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "browser_token",
                table: "sites");

            migrationBuilder.CreateTable(
                name: "site_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_site_tokens_site_id",
                table: "site_tokens",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_tokens_token",
                table: "site_tokens",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_tokens");

            migrationBuilder.AddColumn<string>(
                name: "browser_token",
                table: "sites",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_sites_browser_token",
                table: "sites",
                column: "browser_token",
                unique: true);
        }
    }
}
