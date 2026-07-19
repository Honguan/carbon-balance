using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStageActivityFormulasAndUnitCatalogueV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "activity_amount_formula_id",
                schema: "app",
                table: "calculation_line_items",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "direct-activity-amount-v1");

            migrationBuilder.AddColumn<string>(
                name: "formula_inputs_json",
                schema: "app",
                table: "calculation_line_items",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "amount_formula_id",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "direct-activity-amount-v1");

            migrationBuilder.AddColumn<string>(
                name: "formula_inputs_json",
                schema: "app",
                table: "activity_data_versions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.InsertData(
                schema: "app",
                table: "units",
                columns: new[] { "id", "aliases_csv", "canonical_code", "catalogue_version", "code", "composite_expression", "dimension", "offset_to_canonical", "scale_to_canonical", "symbol" },
                values: new object[,]
                {
                    { new Guid("72000000-0000-0000-0000-000000000001"), "kilogram,kilograms", "kg", "units-p0-v2", "kg", "", "mass", 0m, 1m, "kg" },
                    { new Guid("72000000-0000-0000-0000-000000000002"), "gram,grams", "kg", "units-p0-v2", "g", "", "mass", 0m, 0.001m, "g" },
                    { new Guid("72000000-0000-0000-0000-000000000003"), "ton,tons,tonnes", "kg", "units-p0-v2", "tonne", "", "mass", 0m, 1000m, "t" },
                    { new Guid("72000000-0000-0000-0000-000000000004"), "kilowatt-hour", "kWh", "units-p0-v2", "kWh", "", "energy", 0m, 1m, "kWh" },
                    { new Guid("72000000-0000-0000-0000-000000000005"), "t-km,tkm", "tonne-km", "units-p0-v2", "tonne-km", "tonne*km", "transport-work", 0m, 1m, "t·km" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("72000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("72000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("72000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("72000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("72000000-0000-0000-0000-000000000005"));

            migrationBuilder.DropColumn(
                name: "activity_amount_formula_id",
                schema: "app",
                table: "calculation_line_items");

            migrationBuilder.DropColumn(
                name: "formula_inputs_json",
                schema: "app",
                table: "calculation_line_items");

            migrationBuilder.DropColumn(
                name: "amount_formula_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "formula_inputs_json",
                schema: "app",
                table: "activity_data_versions");
        }
    }
}
