using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernanceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "aliases_csv",
                schema: "app",
                table: "units",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "composite_expression",
                schema: "app",
                table: "units",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "applicability",
                schema: "app",
                table: "pcr_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "legacy-existing-version");

            migrationBuilder.AddColumn<string>(
                name: "ccc_classification",
                schema: "app",
                table: "pcr_versions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "original_document_name",
                schema: "app",
                table: "pcr_versions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "original_document_sha256",
                schema: "app",
                table: "pcr_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "review_status",
                schema: "app",
                table: "pcr_versions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                schema: "app",
                table: "pcr_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                schema: "app",
                table: "pcr_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rule_requirements",
                schema: "app",
                table: "pcr_versions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "standard_code",
                schema: "app",
                table: "pcr_versions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "applicability",
                schema: "app",
                table: "emission_factor_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "legacy-existing-version");

            migrationBuilder.AddColumn<string>(
                name: "dataset_name",
                schema: "app",
                table: "emission_factor_versions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "app",
                table: "emission_factor_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_status",
                schema: "app",
                table: "emission_factor_versions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                schema: "app",
                table: "emission_factor_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                schema: "app",
                table: "emission_factor_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_name",
                schema: "app",
                table: "emission_factor_versions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "withdrawn_at",
                schema: "app",
                table: "emission_factor_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000001"),
                columns: new[] { "aliases_csv", "composite_expression" },
                values: new object[] { "kilogram,kilograms", "" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000002"),
                columns: new[] { "aliases_csv", "composite_expression" },
                values: new object[] { "gram,grams", "" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000003"),
                columns: new[] { "aliases_csv", "composite_expression" },
                values: new object[] { "kilowatt-hour", "" });

            migrationBuilder.UpdateData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000004"),
                columns: new[] { "aliases_csv", "composite_expression" },
                values: new object[] { "t-km,tkm", "tonne*km" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aliases_csv",
                schema: "app",
                table: "units");

            migrationBuilder.DropColumn(
                name: "composite_expression",
                schema: "app",
                table: "units");

            migrationBuilder.DropColumn(
                name: "applicability",
                schema: "app",
                table: "pcr_versions");

            migrationBuilder.DropColumn(
                name: "ccc_classification",
                schema: "app",
                table: "pcr_versions");

            migrationBuilder.DropColumn(
                name: "original_document_name",
                schema: "app",
                table: "pcr_versions");

            migrationBuilder.DropColumn(
                name: "original_document_sha256",
                schema: "app",
                table: "pcr_versions");

            migrationBuilder.DropColumn(
                name: "review_status",
                schema: "app",
                table: "pcr_versions");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "app",
                table: "pcr_versions");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "app",
                table: "pcr_versions");

            migrationBuilder.DropColumn(
                name: "rule_requirements",
                schema: "app",
                table: "pcr_versions");

            migrationBuilder.DropColumn(
                name: "standard_code",
                schema: "app",
                table: "pcr_versions");

            migrationBuilder.DropColumn(
                name: "applicability",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "dataset_name",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "review_status",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "source_name",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "withdrawn_at",
                schema: "app",
                table: "emission_factor_versions");
        }
    }
}
