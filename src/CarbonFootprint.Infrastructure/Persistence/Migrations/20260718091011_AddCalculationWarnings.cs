using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalculationWarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calculation_warnings",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calculation_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calculation_warnings", x => x.id);
                    table.ForeignKey(
                        name: "fk_calculation_warnings_calculation_runs_calculation_run_id",
                        column: x => x.calculation_run_id,
                        principalSchema: "app",
                        principalTable: "calculation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calculation_warnings_calculation_run_id",
                schema: "app",
                table: "calculation_warnings",
                column: "calculation_run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calculation_warnings",
                schema: "app");
        }
    }
}
