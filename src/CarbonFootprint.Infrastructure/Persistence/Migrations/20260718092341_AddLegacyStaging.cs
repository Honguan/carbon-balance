using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "staging");

            migrationBuilder.CreateTable(
                name: "import_batches",
                schema: "staging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    source_file_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    parsed_rows = table.Column<int>(type: "integer", nullable: false),
                    invalid_rows = table.Column<int>(type: "integer", nullable: false),
                    conflict_rows = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rows",
                schema: "staging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_row_number = table.Column<long>(type: "bigint", nullable: false),
                    raw_payload_json = table.Column<string>(type: "text", nullable: false),
                    raw_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    parse_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    validation_errors_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_rows_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalSchema: "staging",
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conflicts",
                schema: "staging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staging_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conflict_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    details_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "fk_conflicts_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalSchema: "staging",
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conflicts_rows_staging_row_id",
                        column: x => x.staging_row_id,
                        principalSchema: "staging",
                        principalTable: "rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conflicts_import_batch_id_conflict_key",
                schema: "staging",
                table: "conflicts",
                columns: new[] { "import_batch_id", "conflict_key" });

            migrationBuilder.CreateIndex(
                name: "ix_conflicts_staging_row_id",
                schema: "staging",
                table: "conflicts",
                column: "staging_row_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_organization_id_source_file_sha256_entity_ty",
                schema: "staging",
                table: "import_batches",
                columns: new[] { "organization_id", "source_file_sha256", "entity_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rows_import_batch_id_source_row_number",
                schema: "staging",
                table: "rows",
                columns: new[] { "import_batch_id", "source_row_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conflicts",
                schema: "staging");

            migrationBuilder.DropTable(
                name: "rows",
                schema: "staging");

            migrationBuilder.DropTable(
                name: "import_batches",
                schema: "staging");
        }
    }
}
