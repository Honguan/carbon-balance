using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLifecycleActivityGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "allocation_factor",
                schema: "app",
                table: "calculation_line_items",
                type: "numeric(18,15)",
                precision: 18,
                scale: 15,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "activity_kind",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Material");

            migrationBuilder.AddColumn<decimal>(
                name: "allocation_factor",
                schema: "app",
                table: "activity_data_versions",
                type: "numeric(18,15)",
                precision: 18,
                scale: 15,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "data_quality",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "legacy-existing");

            migrationBuilder.AddColumn<string>(
                name: "estimation_reason",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_estimated",
                schema: "app",
                table: "activity_data_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "supplier_or_scenario",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "lifecycle_stage_declarations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_project_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifecycle_stage = table.Column<int>(type: "integer", nullable: false),
                    is_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lifecycle_stage_declarations", x => x.id);
                    table.ForeignKey(
                        name: "fk_lifecycle_stage_declarations_inventory_project_versions_inv",
                        column: x => x.inventory_project_version_id,
                        principalSchema: "app",
                        principalTable: "inventory_project_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lifecycle_stage_declarations_inventory_project_version_id_l",
                schema: "app",
                table: "lifecycle_stage_declarations",
                columns: new[] { "inventory_project_version_id", "lifecycle_stage" },
                unique: true);

            migrationBuilder.Sql(
                """
                UPDATE app.activity_data_versions
                SET activity_kind = CASE lifecycle_stage
                    WHEN 1 THEN 'Material'
                    WHEN 2 THEN 'Energy'
                    WHEN 3 THEN 'DistributionTransport'
                    WHEN 4 THEN 'UseEnergy'
                    WHEN 5 THEN 'EndOfLifeTreatment'
                    END;

                INSERT INTO app.lifecycle_stage_declarations
                    (id, organization_id, inventory_project_version_id, lifecycle_stage, is_applicable, reason)
                SELECT gen_random_uuid(), inventory.organization_id, inventory.id, stage.value, TRUE, ''
                FROM app.inventory_project_versions AS inventory
                CROSS JOIN generate_series(1, 5) AS stage(value);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lifecycle_stage_declarations",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "allocation_factor",
                schema: "app",
                table: "calculation_line_items");

            migrationBuilder.DropColumn(
                name: "activity_kind",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "allocation_factor",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "data_quality",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "estimation_reason",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "is_estimated",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "supplier_or_scenario",
                schema: "app",
                table: "activity_data_versions");
        }
    }
}
