using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPcrGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pcr_version_id",
                schema: "app",
                table: "inventory_project_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pcr_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registration_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    publication_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pcr_versions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_project_versions_pcr_version_id",
                schema: "app",
                table: "inventory_project_versions",
                column: "pcr_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_pcr_versions_organization_id_registration_number_version_nu",
                schema: "app",
                table: "pcr_versions",
                columns: new[] { "organization_id", "registration_number", "version_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_project_versions_pcr_versions_pcr_version_id",
                schema: "app",
                table: "inventory_project_versions",
                column: "pcr_version_id",
                principalSchema: "app",
                principalTable: "pcr_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inventory_project_versions_pcr_versions_pcr_version_id",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropTable(
                name: "pcr_versions",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "ix_inventory_project_versions_pcr_version_id",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "pcr_version_id",
                schema: "app",
                table: "inventory_project_versions");
        }
    }
}
