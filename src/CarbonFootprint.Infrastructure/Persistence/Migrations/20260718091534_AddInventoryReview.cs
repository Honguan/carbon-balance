using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_comment",
                schema: "app",
                table: "inventory_project_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reviewed_at",
                schema: "app",
                table: "inventory_project_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                schema: "app",
                table: "inventory_project_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                schema: "app",
                table: "inventory_project_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_project_versions_organization_id_workflow_status",
                schema: "app",
                table: "inventory_project_versions",
                columns: new[] { "organization_id", "workflow_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inventory_project_versions_organization_id_workflow_status",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "review_comment",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                schema: "app",
                table: "inventory_project_versions");
        }
    }
}
