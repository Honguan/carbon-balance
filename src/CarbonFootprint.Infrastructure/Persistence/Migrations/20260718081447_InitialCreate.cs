using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CarbonFootprint.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    resource_type = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    before_hash = table.Column<string>(type: "text", nullable: true),
                    after_hash = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "emission_factor_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    value = table.Column<decimal>(type: "numeric(30,15)", precision: 30, scale: 15, nullable: false),
                    numerator_unit_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    denominator_unit_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    geography = table.Column<string>(type: "text", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    publication_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_dataset_version = table.Column<string>(type: "text", nullable: false),
                    license_code = table.Column<string>(type: "text", nullable: false),
                    supersedes_version_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emission_factor_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_emission_factor_versions_emission_factor_versions_supersede",
                        column: x => x.supersedes_version_id,
                        principalSchema: "app",
                        principalTable: "emission_factor_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    symbol = table.Column<string>(type: "text", nullable: false),
                    dimension = table.Column<string>(type: "text", nullable: false),
                    scale_to_canonical = table.Column<decimal>(type: "numeric(30,15)", precision: 30, scale: 15, nullable: false),
                    offset_to_canonical = table.Column<decimal>(type: "numeric(30,15)", precision: 30, scale: 15, nullable: false),
                    canonical_code = table.Column<string>(type: "text", nullable: false),
                    catalogue_version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_products_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "app",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_memberships",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_memberships_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "app",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "identity",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    name_zh_tw = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_versions_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "app",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_project_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    functional_unit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pcr_version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    workflow_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_project_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_inventory_project_versions_product_versions_product_version",
                        column: x => x.product_version_id,
                        principalSchema: "app",
                        principalTable: "product_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activity_data_versions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_project_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifecycle_stage = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    raw_value = table.Column<decimal>(type: "numeric(30,12)", precision: 30, scale: 12, nullable: false),
                    raw_unit_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    canonical_value = table.Column<decimal>(type: "numeric(30,12)", precision: 30, scale: 12, nullable: false),
                    canonical_unit_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    conversion_rule_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    factor_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_data_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_data_versions_emission_factor_versions_factor_vers",
                        column: x => x.factor_version_id,
                        principalSchema: "app",
                        principalTable: "emission_factor_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activity_data_versions_inventory_project_versions_inventory",
                        column: x => x.inventory_project_version_id,
                        principalSchema: "app",
                        principalTable: "inventory_project_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calculation_runs",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supersedes_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canonical_input_manifest = table.Column<string>(type: "jsonb", nullable: false),
                    input_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    engine_build = table.Column<string>(type: "text", nullable: false),
                    rule_set_version = table.Column<string>(type: "text", nullable: false),
                    unit_catalogue_version = table.Column<string>(type: "text", nullable: false),
                    gwp_version = table.Column<string>(type: "text", nullable: false),
                    pcr_version = table.Column<string>(type: "text", nullable: false),
                    product_total = table.Column<decimal>(type: "numeric(38,15)", precision: 38, scale: 15, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calculation_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_calculation_runs_calculation_runs_supersedes_run_id",
                        column: x => x.supersedes_run_id,
                        principalSchema: "app",
                        principalTable: "calculation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_calculation_runs_inventory_project_versions_project_version",
                        column: x => x.project_version_id,
                        principalSchema: "app",
                        principalTable: "inventory_project_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calculation_line_items",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calculation_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifecycle_stage = table.Column<int>(type: "integer", nullable: false),
                    formula_id = table.Column<string>(type: "text", nullable: false),
                    canonical_activity_value = table.Column<decimal>(type: "numeric(30,12)", precision: 30, scale: 12, nullable: false),
                    activity_unit_code = table.Column<string>(type: "text", nullable: false),
                    factor_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factor_value = table.Column<decimal>(type: "numeric(30,15)", precision: 30, scale: 15, nullable: false),
                    factor_unit = table.Column<string>(type: "text", nullable: false),
                    emissions = table.Column<decimal>(type: "numeric(38,15)", precision: 38, scale: 15, nullable: false),
                    emissions_unit_code = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calculation_line_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_calculation_line_items_calculation_runs_calculation_run_id",
                        column: x => x.calculation_run_id,
                        principalSchema: "app",
                        principalTable: "calculation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calculation_stage_summaries",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calculation_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifecycle_stage = table.Column<int>(type: "integer", nullable: false),
                    emissions = table.Column<decimal>(type: "numeric(38,15)", precision: 38, scale: 15, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calculation_stage_summaries", x => x.id);
                    table.ForeignKey(
                        name: "fk_calculation_stage_summaries_calculation_runs_calculation_ru",
                        column: x => x.calculation_run_id,
                        principalSchema: "app",
                        principalTable: "calculation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_data_versions_factor_version_id",
                schema: "app",
                table: "activity_data_versions",
                column: "factor_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_data_versions_inventory_project_version_id",
                schema: "app",
                table: "activity_data_versions",
                column: "inventory_project_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_organization_id_timestamp",
                schema: "app",
                table: "audit_events",
                columns: new[] { "organization_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_calculation_line_items_calculation_run_id",
                schema: "app",
                table: "calculation_line_items",
                column: "calculation_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_calculation_runs_organization_id_input_sha256",
                schema: "app",
                table: "calculation_runs",
                columns: new[] { "organization_id", "input_sha256" });

            migrationBuilder.CreateIndex(
                name: "ix_calculation_runs_project_version_id",
                schema: "app",
                table: "calculation_runs",
                column: "project_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_calculation_runs_supersedes_run_id",
                schema: "app",
                table: "calculation_runs",
                column: "supersedes_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_calculation_stage_summaries_calculation_run_id_lifecycle_st",
                schema: "app",
                table: "calculation_stage_summaries",
                columns: new[] { "calculation_run_id", "lifecycle_stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_emission_factor_versions_factor_id_version_number",
                schema: "app",
                table: "emission_factor_versions",
                columns: new[] { "factor_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_emission_factor_versions_supersedes_version_id",
                schema: "app",
                table: "emission_factor_versions",
                column: "supersedes_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_project_versions_product_version_id_version_number",
                schema: "app",
                table: "inventory_project_versions",
                columns: new[] { "product_version_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_memberships_organization_id_user_id",
                schema: "app",
                table: "organization_memberships",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_memberships_user_id",
                schema: "app",
                table: "organization_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_versions_product_id_version_number",
                schema: "app",
                table: "product_versions",
                columns: new[] { "product_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_organization_id",
                schema: "app",
                table: "products",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                schema: "identity",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identity",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_units_code_catalogue_version",
                schema: "app",
                table: "units",
                columns: new[] { "code", "catalogue_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "identity",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "identity",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "identity",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_data_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "calculation_line_items",
                schema: "app");

            migrationBuilder.DropTable(
                name: "calculation_stage_summaries",
                schema: "app");

            migrationBuilder.DropTable(
                name: "organization_memberships",
                schema: "app");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "units",
                schema: "app");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "emission_factor_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "calculation_runs",
                schema: "app");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "inventory_project_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "product_versions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "products",
                schema: "app");

            migrationBuilder.DropTable(
                name: "organizations",
                schema: "app");
        }
    }
}
