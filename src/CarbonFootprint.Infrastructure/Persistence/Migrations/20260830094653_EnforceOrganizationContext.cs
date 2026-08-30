using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOrganizationContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM app.organization_memberships
                        WHERE revoked_at IS NULL
                        GROUP BY user_id
                        HAVING COUNT(*) > 1)
                    THEN
                        RAISE EXCEPTION 'Cannot enforce one active organization per user: resolve duplicate active memberships first.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                "DELETE FROM identity.user_claims WHERE claim_type = 'organization_id';");

            migrationBuilder.DropIndex(
                name: "ix_organization_memberships_user_id",
                schema: "app",
                table: "organization_memberships");

            migrationBuilder.CreateIndex(
                name: "ux_organization_memberships_active_user",
                schema: "app",
                table: "organization_memberships",
                column: "user_id",
                unique: true,
                filter: "revoked_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM identity.user_claims WHERE claim_type = 'organization_id';
                INSERT INTO identity.user_claims (user_id, claim_type, claim_value)
                SELECT user_id, 'organization_id', organization_id::text
                FROM app.organization_memberships
                WHERE revoked_at IS NULL;
                """);

            migrationBuilder.DropIndex(
                name: "ux_organization_memberships_active_user",
                schema: "app",
                table: "organization_memberships");

            migrationBuilder.CreateIndex(
                name: "ix_organization_memberships_user_id",
                schema: "app",
                table: "organization_memberships",
                column: "user_id");
        }
    }
}
