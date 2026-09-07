using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireActivityVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retired_at",
                schema: "app",
                table: "activity_data_versions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM app.activity_data_versions WHERE retired_at IS NOT NULL) THEN
                        RAISE EXCEPTION 'Activity retirement has been used. Restore the pre-upgrade backup before rolling back to avoid reactivating superseded data.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "retired_at",
                schema: "app",
                table: "activity_data_versions");
        }
    }
}
