using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFactorSourceDocumentIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "original_document_name",
                schema: "app",
                table: "emission_factor_versions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "original_document_sha256",
                schema: "app",
                table: "emission_factor_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_reference",
                schema: "app",
                table: "emission_factor_versions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                schema: "app",
                table: "emission_factor_versions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "original_document_name",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "original_document_sha256",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "source_reference",
                schema: "app",
                table: "emission_factor_versions");

            migrationBuilder.DropColumn(
                name: "source_type",
                schema: "app",
                table: "emission_factor_versions");
        }
    }
}
