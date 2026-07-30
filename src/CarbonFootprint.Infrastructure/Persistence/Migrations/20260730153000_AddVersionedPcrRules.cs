using System;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CarbonFootprintDbContext))]
[Migration("20260730153000_AddVersionedPcrRules")]
public sealed class AddVersionedPcrRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "approval_date",
            schema: "app",
            table: "pcr_versions",
            type: "date",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "cutoff_threshold_percent",
            schema: "app",
            table: "pcr_versions",
            type: "numeric(9,6)",
            precision: 9,
            scale: 6,
            nullable: false,
            defaultValue: 0m);
        migrationBuilder.AddColumn<string>(
            name: "custom_approval_status",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "NotRequired");
        migrationBuilder.AddColumn<string>(
            name: "custom_rule_justification",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<Guid>(
            name: "created_by",
            schema: "app",
            table: "pcr_versions",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "deprecated_at",
            schema: "app",
            table: "pcr_versions",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "deprecation_reason",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "declared_unit_code",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "*");
        migrationBuilder.AddColumn<string>(
            name: "formula_rule_set_version",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "legacy-stage-formulas-pending-review-v1");
        migrationBuilder.AddColumn<string>(
            name: "functional_unit_pattern",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(500)",
            maxLength: 500,
            nullable: false,
            defaultValue: "*");
        migrationBuilder.AddColumn<bool>(
            name: "is_custom_rule",
            schema: "app",
            table: "pcr_versions",
            type: "boolean",
            nullable: false,
            defaultValue: false);
        migrationBuilder.AddColumn<string>(
            name: "original_document_content_type",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "original_document_object_key",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "original_document_scan_status",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "LegacyUnverified");
        migrationBuilder.AddColumn<long>(
            name: "original_document_size_bytes",
            schema: "app",
            table: "pcr_versions",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
        migrationBuilder.AddColumn<string>(
            name: "permitted_allocation_methods_csv",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "product_category_patterns",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: false,
            defaultValue: "*");
        migrationBuilder.AddColumn<string>(
            name: "reporting_requirements",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<int>(
            name: "rounding_decimal_places",
            schema: "app",
            table: "pcr_versions",
            type: "integer",
            nullable: false,
            defaultValue: 3);
        migrationBuilder.AddColumn<Guid>(
            name: "rule_set_id",
            schema: "app",
            table: "pcr_versions",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);
        migrationBuilder.AddColumn<Guid>(
            name: "supersedes_version_id",
            schema: "app",
            table: "pcr_versions",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "system_boundary_code",
            schema: "app",
            table: "pcr_versions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "*");

        migrationBuilder.InsertData(
            schema: "app",
            table: "units",
            columns: new[]
            {
                "id", "aliases_csv", "canonical_code", "catalogue_version", "code",
                "composite_expression", "dimension", "offset_to_canonical",
                "scale_to_canonical", "symbol"
            },
            columnTypes: new[]
            {
                "uuid", "character varying(500)", "text", "text", "text",
                "character varying(200)", "text", "numeric(30,15)",
                "numeric(30,15)", "text"
            },
            values: new object[]
            {
                new Guid("72000000-0000-0000-0000-000000000006"),
                "pieces,item,items,件,個",
                "piece",
                "units-p0-v2",
                "piece",
                "",
                "count",
                0m,
                1m,
                "pc"
            });

        migrationBuilder.Sql(
            """
            WITH rule_sets AS (
                SELECT organization_id,
                       registration_number,
                       (array_agg(id ORDER BY version_number, id))[1] AS rule_set_id
                FROM app.pcr_versions
                GROUP BY organization_id, registration_number
            )
            UPDATE app.pcr_versions AS p
            SET rule_set_id = r.rule_set_id,
                reporting_requirements = p.rule_requirements
            FROM rule_sets AS r
            WHERE p.organization_id = r.organization_id
              AND p.registration_number = r.registration_number;

            WITH version_chain AS (
                SELECT id,
                       lag(id) OVER (
                           PARTITION BY organization_id, registration_number
                           ORDER BY version_number, id) AS predecessor_id
                FROM app.pcr_versions
            )
            UPDATE app.pcr_versions AS p
            SET supersedes_version_id = chain.predecessor_id
            FROM version_chain AS chain
            WHERE p.id = chain.id
              AND chain.predecessor_id IS NOT NULL;
            """);

        migrationBuilder.CreateTable(
            name: "pcr_stage_rules",
            schema: "app",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                pcr_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                lifecycle_stage = table.Column<int>(type: "integer", nullable: false),
                requirement = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                permitted_activity_kinds_csv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                required_fields_csv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pcr_stage_rules", x => x.id);
                table.ForeignKey(
                    name: "fk_pcr_stage_rules_pcr_versions_pcr_version_id",
                    column: x => x.pcr_version_id,
                    principalSchema: "app",
                    principalTable: "pcr_versions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql(
            """
            INSERT INTO app.pcr_stage_rules
                (id, organization_id, pcr_version_id, lifecycle_stage, requirement, permitted_activity_kinds_csv, required_fields_csv)
            SELECT gen_random_uuid(),
                   p.organization_id,
                   p.id,
                   stage.lifecycle_stage,
                   'Optional',
                   stage.permitted_kinds,
                   ''
            FROM app.pcr_versions p
            CROSS JOIN (
                VALUES
                    (1, 'Material,MaterialTransport'),
                    (2, 'Energy,ManufacturingWaste,OutsourcedTreatmentTransport'),
                    (3, 'DistributionTransport'),
                    (4, 'UseEnergy,UseConsumable'),
                    (5, 'EndOfLifeTreatment,EndOfLifeTransport')
            ) AS stage(lifecycle_stage, permitted_kinds);
            """);

        migrationBuilder.CreateIndex(
            name: "ix_pcr_stage_rules_pcr_version_id_lifecycle_stage",
            schema: "app",
            table: "pcr_stage_rules",
            columns: new[] { "pcr_version_id", "lifecycle_stage" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_pcr_versions_created_by",
            schema: "app",
            table: "pcr_versions",
            column: "created_by");
        migrationBuilder.CreateIndex(
            name: "ix_pcr_versions_organization_id_rule_set_id_version_number",
            schema: "app",
            table: "pcr_versions",
            columns: new[] { "organization_id", "rule_set_id", "version_number" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_pcr_versions_supersedes_version_id",
            schema: "app",
            table: "pcr_versions",
            column: "supersedes_version_id");
        migrationBuilder.AddForeignKey(
            name: "fk_pcr_versions_users_created_by",
            schema: "app",
            table: "pcr_versions",
            column: "created_by",
            principalSchema: "identity",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "fk_pcr_versions_pcr_versions_supersedes_version_id",
            schema: "app",
            table: "pcr_versions",
            column: "supersedes_version_id",
            principalSchema: "app",
            principalTable: "pcr_versions",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            schema: "app",
            table: "units",
            keyColumn: "id",
            keyColumnType: "uuid",
            keyValue: new Guid("72000000-0000-0000-0000-000000000006"));

        migrationBuilder.DropForeignKey(
            name: "fk_pcr_versions_users_created_by",
            schema: "app",
            table: "pcr_versions");
        migrationBuilder.DropForeignKey(
            name: "fk_pcr_versions_pcr_versions_supersedes_version_id",
            schema: "app",
            table: "pcr_versions");
        migrationBuilder.DropTable(name: "pcr_stage_rules", schema: "app");
        migrationBuilder.DropIndex(
            name: "ix_pcr_versions_created_by",
            schema: "app",
            table: "pcr_versions");
        migrationBuilder.DropIndex(
            name: "ix_pcr_versions_organization_id_rule_set_id_version_number",
            schema: "app",
            table: "pcr_versions");
        migrationBuilder.DropIndex(
            name: "ix_pcr_versions_supersedes_version_id",
            schema: "app",
            table: "pcr_versions");

        foreach (var column in new[]
                 {
                     "approval_date", "cutoff_threshold_percent", "custom_approval_status",
                     "custom_rule_justification", "created_by", "deprecated_at", "deprecation_reason",
                     "declared_unit_code", "formula_rule_set_version", "functional_unit_pattern",
                     "is_custom_rule", "original_document_content_type", "original_document_object_key",
                     "original_document_scan_status", "original_document_size_bytes",
                     "permitted_allocation_methods_csv", "product_category_patterns",
                     "reporting_requirements", "rounding_decimal_places", "rule_set_id",
                     "supersedes_version_id", "system_boundary_code"
                 })
        {
            migrationBuilder.DropColumn(name: column, schema: "app", table: "pcr_versions");
        }
    }
}
