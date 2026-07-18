using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedUnitCatalogueV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "app",
                table: "units",
                columns: new[] { "id", "canonical_code", "catalogue_version", "code", "dimension", "offset_to_canonical", "scale_to_canonical", "symbol" },
                values: new object[,]
                {
                    { new Guid("71000000-0000-0000-0000-000000000001"), "kg", "units-p0-v1", "kg", "mass", 0m, 1m, "kg" },
                    { new Guid("71000000-0000-0000-0000-000000000002"), "kg", "units-p0-v1", "g", "mass", 0m, 0.001m, "g" },
                    { new Guid("71000000-0000-0000-0000-000000000003"), "kWh", "units-p0-v1", "kWh", "energy", 0m, 1m, "kWh" },
                    { new Guid("71000000-0000-0000-0000-000000000004"), "tonne-km", "units-p0-v1", "tonne-km", "transport-work", 0m, 1m, "t·km" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                schema: "app",
                table: "units",
                keyColumn: "id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000004"));
        }
    }
}
