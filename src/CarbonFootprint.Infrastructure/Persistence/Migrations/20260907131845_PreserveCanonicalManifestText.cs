using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreserveCanonicalManifestText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "canonical_input_manifest",
                schema: "app",
                table: "calculation_runs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM app.calculation_runs) THEN
                        RAISE EXCEPTION 'Canonical manifests must remain text. Restore the pre-upgrade backup to roll back without losing original bytes.';
                    END IF;
                END $$;
                ALTER TABLE app.calculation_runs ALTER COLUMN canonical_input_manifest
                    TYPE jsonb USING canonical_input_manifest::jsonb;
                """);
        }
    }
}
