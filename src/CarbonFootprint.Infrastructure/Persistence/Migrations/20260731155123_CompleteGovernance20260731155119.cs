using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteGovernance20260731155119 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "app",
                table: "inventory_project_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "formula_definition_version_id",
                schema: "app",
                table: "calculation_line_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "formula_trace_json",
                schema: "app",
                table: "calculation_line_items",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "governance_trace_json",
                schema: "app",
                table: "calculation_line_items",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "factor_version_id",
                schema: "app",
                table: "activity_data_versions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "allocation_governance_record_id",
                schema: "app",
                table: "activity_data_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "data_quality_governance_record_id",
                schema: "app",
                table: "activity_data_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "formula_definition_version_id",
                schema: "app",
                table: "activity_data_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "formula_trace_json",
                schema: "app",
                table: "activity_data_versions",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "global_factor_definition_version_id",
                schema: "app",
                table: "activity_data_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "governance_trace_json",
                schema: "app",
                table: "activity_data_versions",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "transport_governance_record_id",
                schema: "app",
                table: "activity_data_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "evidence_documents",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    coverage_start = table.Column<DateOnly>(type: "date", nullable: true),
                    coverage_end = table.Column<DateOnly>(type: "date", nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "governance_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    payload_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_governance_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_governance_events_inventory_project_versions_project_versio",
                        column: x => x.project_version_id,
                        principalSchema: "app",
                        principalTable: "inventory_project_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_governance_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    record_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stable_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    canonical_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_immutable = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lock_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_governance_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_governance_versions_inventory_project_versions_proj",
                        column: x => x.project_version_id,
                        principalSchema: "app",
                        principalTable: "inventory_project_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_impacts",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dependency_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dependency_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    previous_version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    current_version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    affected_emissions = table.Column<decimal>(type: "numeric(38,15)", precision: 38, scale: 15, nullable: false),
                    lifecycle_stage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_impacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_impacts_inventory_project_versions_project_version_",
                        column: x => x.project_version_id,
                        principalSchema: "app",
                        principalTable: "inventory_project_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verification_archives",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calculation_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    export_schema_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    archive_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    archive_bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    file_index_json = table.Column<string>(type: "jsonb", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_archives", x => x.id);
                    table.ForeignKey(
                        name: "fk_verification_archives_calculation_runs_calculation_run_id",
                        column: x => x.calculation_run_id,
                        principalSchema: "app",
                        principalTable: "calculation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_verification_archives_inventory_project_versions_project_ve",
                        column: x => x.project_version_id,
                        principalSchema: "app",
                        principalTable: "inventory_project_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence_document_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    object_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    object_storage_version = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scan_engine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scan_engine_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scan_signature_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scan_details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    storage_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    replaces_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_document_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_evidence_document_versions_evidence_document_versions_repla",
                        column: x => x.replaces_version_id,
                        principalSchema: "app",
                        principalTable: "evidence_document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evidence_document_versions_evidence_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "app",
                        principalTable: "evidence_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence_access_logs",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_access_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_evidence_access_logs_evidence_document_versions_document_ve",
                        column: x => x.document_version_id,
                        principalSchema: "app",
                        principalTable: "evidence_document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence_links",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    linked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_evidence_links_evidence_document_versions_document_version_",
                        column: x => x.document_version_id,
                        principalSchema: "app",
                        principalTable: "evidence_document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "governance_definition_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    definition_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stable_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    publication_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    canonical_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_stable_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    source_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    source_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    source_dataset_version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    license_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    source_evidence_document_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supersedes_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_governance_definition_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_governance_definition_versions_evidence_document_versions_s",
                        column: x => x.source_evidence_document_version_id,
                        principalSchema: "app",
                        principalTable: "evidence_document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_governance_definition_versions_governance_definition_versio",
                        column: x => x.supersedes_version_id,
                        principalSchema: "app",
                        principalTable: "governance_definition_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence_retention_locks",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_definition_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trigger = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    locked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_legal_hold = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_retention_locks", x => x.id);
                    table.ForeignKey(
                        name: "fk_evidence_retention_locks_evidence_document_versions_documen",
                        column: x => x.document_version_id,
                        principalSchema: "app",
                        principalTable: "evidence_document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evidence_retention_locks_governance_definition_versions_pol",
                        column: x => x.policy_definition_version_id,
                        principalSchema: "app",
                        principalTable: "governance_definition_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organization_definition_activations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_prohibited = table.Column<bool>(type: "boolean", nullable: false),
                    display_alias = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    internal_category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    applicability_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    override_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_definition_activations", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_definition_activations_governance_definition_v",
                        column: x => x.definition_version_id,
                        principalSchema: "app",
                        principalTable: "governance_definition_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_data_versions_allocation_governance_record_id",
                schema: "app",
                table: "activity_data_versions",
                column: "allocation_governance_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_data_versions_data_quality_governance_record_id",
                schema: "app",
                table: "activity_data_versions",
                column: "data_quality_governance_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_data_versions_formula_definition_version_id",
                schema: "app",
                table: "activity_data_versions",
                column: "formula_definition_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_data_versions_global_factor_definition_version_id",
                schema: "app",
                table: "activity_data_versions",
                column: "global_factor_definition_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_data_versions_transport_governance_record_id",
                schema: "app",
                table: "activity_data_versions",
                column: "transport_governance_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_access_logs_document_version_id",
                schema: "app",
                table: "evidence_access_logs",
                column: "document_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_access_logs_organization_id_document_version_id_oc",
                schema: "app",
                table: "evidence_access_logs",
                columns: new[] { "organization_id", "document_version_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_document_versions_document_id_version_number",
                schema: "app",
                table: "evidence_document_versions",
                columns: new[] { "document_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evidence_document_versions_organization_id_sha256_size_bytes",
                schema: "app",
                table: "evidence_document_versions",
                columns: new[] { "organization_id", "sha256", "size_bytes" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_document_versions_replaces_version_id",
                schema: "app",
                table: "evidence_document_versions",
                column: "replaces_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_documents_organization_id_created_at",
                schema: "app",
                table: "evidence_documents",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_links_document_version_id",
                schema: "app",
                table: "evidence_links",
                column: "document_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_links_organization_id_document_version_id_target_t",
                schema: "app",
                table: "evidence_links",
                columns: new[] { "organization_id", "document_version_id", "target_type", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evidence_retention_locks_document_version_id_locked_at",
                schema: "app",
                table: "evidence_retention_locks",
                columns: new[] { "document_version_id", "locked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_retention_locks_policy_definition_version_id",
                schema: "app",
                table: "evidence_retention_locks",
                column: "policy_definition_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_governance_definition_versions_definition_type_source_stabl",
                schema: "app",
                table: "governance_definition_versions",
                columns: new[] { "definition_type", "source_stable_id", "version_number" });

            migrationBuilder.CreateIndex(
                name: "ix_governance_definition_versions_definition_type_stable_key_v",
                schema: "app",
                table: "governance_definition_versions",
                columns: new[] { "definition_type", "stable_key", "version_number", "organization_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_governance_definition_versions_source_evidence_document_ver",
                schema: "app",
                table: "governance_definition_versions",
                column: "source_evidence_document_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_governance_definition_versions_supersedes_version_id",
                schema: "app",
                table: "governance_definition_versions",
                column: "supersedes_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_governance_events_organization_id_project_version_id_occurr",
                schema: "app",
                table: "governance_events",
                columns: new[] { "organization_id", "project_version_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_governance_events_project_version_id",
                schema: "app",
                table: "governance_events",
                column: "project_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_definition_activations_definition_version_id",
                schema: "app",
                table: "organization_definition_activations",
                column: "definition_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_definition_activations_organization_id_definit",
                schema: "app",
                table: "organization_definition_activations",
                columns: new[] { "organization_id", "definition_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_governance_versions_organization_id_project_version",
                schema: "app",
                table: "project_governance_versions",
                columns: new[] { "organization_id", "project_version_id", "record_type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_project_governance_versions_project_version_id_record_type_",
                schema: "app",
                table: "project_governance_versions",
                columns: new[] { "project_version_id", "record_type", "stable_key", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_impacts_organization_id_project_version_id_detected",
                schema: "app",
                table: "project_impacts",
                columns: new[] { "organization_id", "project_version_id", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_project_impacts_project_version_id",
                schema: "app",
                table: "project_impacts",
                column: "project_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_archives_calculation_run_id",
                schema: "app",
                table: "verification_archives",
                column: "calculation_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_archives_project_version_id_calculation_run_id",
                schema: "app",
                table: "verification_archives",
                columns: new[] { "project_version_id", "calculation_run_id", "archive_sha256" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_data_versions_governance_definitions_formula_defin",
                schema: "app",
                table: "activity_data_versions",
                column: "formula_definition_version_id",
                principalSchema: "app",
                principalTable: "governance_definition_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_data_versions_governance_definitions_global_factor",
                schema: "app",
                table: "activity_data_versions",
                column: "global_factor_definition_version_id",
                principalSchema: "app",
                principalTable: "governance_definition_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_data_versions_project_governance_records_allocatio",
                schema: "app",
                table: "activity_data_versions",
                column: "allocation_governance_record_id",
                principalSchema: "app",
                principalTable: "project_governance_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_data_versions_project_governance_records_data_qual",
                schema: "app",
                table: "activity_data_versions",
                column: "data_quality_governance_record_id",
                principalSchema: "app",
                principalTable: "project_governance_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_data_versions_project_governance_records_transport",
                schema: "app",
                table: "activity_data_versions",
                column: "transport_governance_record_id",
                principalSchema: "app",
                principalTable: "project_governance_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_data_versions_governance_definitions_formula_defin",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_activity_data_versions_governance_definitions_global_factor",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_activity_data_versions_project_governance_records_allocatio",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_activity_data_versions_project_governance_records_data_qual",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_activity_data_versions_project_governance_records_transport",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropTable(
                name: "evidence_access_logs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "evidence_links",
                schema: "app");

            migrationBuilder.DropTable(
                name: "evidence_retention_locks",
                schema: "app");

            migrationBuilder.DropTable(
                name: "governance_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "organization_definition_activations",
                schema: "app");

            migrationBuilder.DropTable(
                name: "project_governance_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "project_impacts",
                schema: "app");

            migrationBuilder.DropTable(
                name: "verification_archives",
                schema: "app");

            migrationBuilder.DropTable(
                name: "governance_definition_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "evidence_document_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "evidence_documents",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "ix_activity_data_versions_allocation_governance_record_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropIndex(
                name: "ix_activity_data_versions_data_quality_governance_record_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropIndex(
                name: "ix_activity_data_versions_formula_definition_version_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropIndex(
                name: "ix_activity_data_versions_global_factor_definition_version_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropIndex(
                name: "ix_activity_data_versions_transport_governance_record_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "app",
                table: "inventory_project_versions");

            migrationBuilder.DropColumn(
                name: "formula_definition_version_id",
                schema: "app",
                table: "calculation_line_items");

            migrationBuilder.DropColumn(
                name: "formula_trace_json",
                schema: "app",
                table: "calculation_line_items");

            migrationBuilder.DropColumn(
                name: "governance_trace_json",
                schema: "app",
                table: "calculation_line_items");

            migrationBuilder.DropColumn(
                name: "allocation_governance_record_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "data_quality_governance_record_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "formula_definition_version_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "formula_trace_json",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "global_factor_definition_version_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "governance_trace_json",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.DropColumn(
                name: "transport_governance_record_id",
                schema: "app",
                table: "activity_data_versions");

            migrationBuilder.AlterColumn<Guid>(
                name: "factor_version_id",
                schema: "app",
                table: "activity_data_versions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
