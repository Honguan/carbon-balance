using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecureAdministratorBootstrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "administrator_bootstrap",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    claimed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_administrator_bootstrap", x => x.id);
                    table.CheckConstraint("ck_administrator_bootstrap_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "system_audit_events",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_audit_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_system_audit_events_timestamp",
                schema: "identity",
                table: "system_audit_events",
                column: "timestamp");

            migrationBuilder.Sql("""
                INSERT INTO identity.administrator_bootstrap
                    (id, claimed_by_user_id, claimed_at, source, correlation_id)
                SELECT
                    1,
                    user_role.user_id,
                    NOW(),
                    'migration-existing-administrator',
                    'migration-secure-administrator-bootstrap'
                FROM identity.user_roles AS user_role
                INNER JOIN identity.roles AS role ON role.id = user_role.role_id
                WHERE role.normalized_name = 'ADMINISTRATOR'
                ORDER BY user_role.user_id
                LIMIT 1;

                INSERT INTO identity.system_audit_events
                    (id, timestamp, actor_id, action, resource_type, resource_id, source, correlation_id, metadata_json)
                SELECT
                    '43f00000-0000-0000-0000-000000000001',
                    claimed_at,
                    claimed_by_user_id,
                    'identity.administrator.bootstrap_closed_existing',
                    'ApplicationUser',
                    claimed_by_user_id,
                    source,
                    correlation_id,
                    '{}'
                FROM identity.administrator_bootstrap
                WHERE source = 'migration-existing-administrator';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "administrator_bootstrap",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "system_audit_events",
                schema: "identity");
        }
    }
}
