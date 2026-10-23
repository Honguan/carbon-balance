using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionalAuditContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "after_hash",
                schema: "identity",
                table: "system_audit_events",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION app.reject_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Audit records are append-only: UPDATE, DELETE and TRUNCATE are forbidden.'
                        USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER audit_events_no_rewrite BEFORE UPDATE OR DELETE ON app.audit_events
                    FOR EACH ROW EXECUTE FUNCTION app.reject_audit_mutation();
                CREATE TRIGGER audit_events_no_truncate BEFORE TRUNCATE ON app.audit_events
                    FOR EACH STATEMENT EXECUTE FUNCTION app.reject_audit_mutation();
                CREATE TRIGGER system_audit_events_no_rewrite BEFORE UPDATE OR DELETE ON identity.system_audit_events
                    FOR EACH ROW EXECUTE FUNCTION app.reject_audit_mutation();
                CREATE TRIGGER system_audit_events_no_truncate BEFORE TRUNCATE ON identity.system_audit_events
                    FOR EACH STATEMENT EXECUTE FUNCTION app.reject_audit_mutation();
                REVOKE UPDATE, DELETE, TRUNCATE ON app.audit_events, identity.system_audit_events FROM PUBLIC;
                """);

            migrationBuilder.AddColumn<string>(
                name: "before_hash",
                schema: "identity",
                table: "system_audit_events",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM app.audit_events WHERE metadata_json->>'schemaVersion' = 'audit-v1')
                        OR EXISTS (SELECT 1 FROM identity.system_audit_events WHERE metadata_json->>'schemaVersion' = 'audit-v1') THEN
                        RAISE EXCEPTION 'Versioned audit evidence exists. Restore the pre-upgrade backup for rollback without losing evidence protection.';
                    END IF;
                END $$;
                DROP TRIGGER audit_events_no_rewrite ON app.audit_events;
                DROP TRIGGER audit_events_no_truncate ON app.audit_events;
                DROP TRIGGER system_audit_events_no_rewrite ON identity.system_audit_events;
                DROP TRIGGER system_audit_events_no_truncate ON identity.system_audit_events;
                DROP FUNCTION app.reject_audit_mutation();
                """);
            migrationBuilder.DropColumn(
                name: "after_hash",
                schema: "identity",
                table: "system_audit_events");

            migrationBuilder.DropColumn(
                name: "before_hash",
                schema: "identity",
                table: "system_audit_events");
        }
    }
}
