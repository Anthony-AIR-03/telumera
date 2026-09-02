using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Telumera.Services.Analytics.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAggregationCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aggregation_checkpoints",
                columns: table => new
                {
                    job_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    watermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aggregation_checkpoints", x => x.job_name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aggregation_checkpoints");
        }
    }
}
