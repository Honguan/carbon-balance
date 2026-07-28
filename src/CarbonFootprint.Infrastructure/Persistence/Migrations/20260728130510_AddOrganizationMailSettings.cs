using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationMailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organization_mail_settings",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    enable_ssl = table.Column<bool>(type: "boolean", nullable: false),
                    username = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    encrypted_password = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    from_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_mail_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_mail_settings_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "app",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_mail_settings_users_updated_by",
                        column: x => x.updated_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_organization_mail_settings_organization_id",
                schema: "app",
                table: "organization_mail_settings",
                column: "organization_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_mail_settings_updated_by",
                schema: "app",
                table: "organization_mail_settings",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_mail_settings",
                schema: "app");
        }
    }
}
