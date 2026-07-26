using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivitySourceTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "collection_method",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "data_provider",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "data_source_type",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "equipment_category",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_reference",
                schema: "app",
                table: "activity_data_versions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "collection_method",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "data_provider",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "data_source_type",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "equipment_category",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "source_reference",
                schema: "app",
                table: "activity_data_versions");
        }
    }
}
