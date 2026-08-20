using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telumera.Services.SiteRegistry.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteModuleSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "site_module_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_module_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_site_module_settings_site_id_module",
                table: "site_module_settings",
                columns: new[] { "site_id", "module" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_module_settings");
        }
    }
}
