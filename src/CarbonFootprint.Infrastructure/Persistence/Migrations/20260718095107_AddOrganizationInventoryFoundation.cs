using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationInventoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category_code",
                schema: "app",
                table: "products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "facility_id",
                schema: "app",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "allocation_method",
                schema: "app",
                table: "inventory_project_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "allocation_reason",
                schema: "app",
                table: "inventory_project_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "assumptions",
                schema: "app",
                table: "inventory_project_versions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "declared_unit",
                schema: "app",
                table: "inventory_project_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "estimation_reason",
                schema: "app",
                table: "inventory_project_versions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "exclusions",
                schema: "app",
                table: "inventory_project_versions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "system_boundary",
                schema: "app",
                table: "inventory_project_versions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "facilities",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facilities", x => x.id);
                    table.ForeignKey(
                        name: "fk_facilities_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "app",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organization_invitations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_invitations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "app",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_invitations_users_invited_by",
                        column: x => x.invited_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_facility_id",
                schema: "app",
                table: "products",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_facilities_organization_id_code",
                schema: "app",
                table: "facilities",
                columns: new[] { "organization_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_invitations_invited_by",
                schema: "app",
                table: "organization_invitations",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "ix_organization_invitations_organization_id_email",
                schema: "app",
                table: "organization_invitations",
                columns: new[] { "organization_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_organization_invitations_token_sha256",
                schema: "app",
                table: "organization_invitations",
                column: "token_sha256",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_products_facilities_facility_id",
                schema: "app",
                table: "products",
                column: "facility_id",
                principalSchema: "app",
                principalTable: "facilities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_products_facilities_facility_id",
                schema: "app",
                table: "products");

            migrationBuilder.DropTable(
                name: "facilities",
                schema: "app");

            migrationBuilder.DropTable(
                name: "organization_invitations",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "ix_products_facility_id",
                schema: "app",
                table: "products");

            migrationBuilder.DropColumn(
                name: "category_code",
                schema: "app",
                table: "products");

            migrationBuilder.DropColumn(
                name: "facility_id",
                schema: "app",
                table: "products");

            migrationBuilder.DropColumn(
                name: "allocation_method",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "allocation_reason",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "assumptions",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "declared_unit",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "estimation_reason",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "exclusions",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "system_boundary",
                schema: "app",
                table: "inventory_project_versions");
        }
    }
}
