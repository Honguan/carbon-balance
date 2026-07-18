CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'app') THEN
            CREATE SCHEMA app;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'identity') THEN
            CREATE SCHEMA identity;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.audit_events (
        id uuid NOT NULL,
        timestamp timestamp with time zone NOT NULL,
        actor_id uuid,
        organization_id uuid NOT NULL,
        action text NOT NULL,
        resource_type text NOT NULL,
        resource_id uuid NOT NULL,
        before_hash text,
        after_hash text,
        correlation_id character varying(100) NOT NULL,
        metadata_json jsonb NOT NULL,
        CONSTRAINT pk_audit_events PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.emission_factor_versions (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        factor_id uuid NOT NULL,
        version_number integer NOT NULL,
        name character varying(500) NOT NULL,
        value numeric(30,15) NOT NULL,
        numerator_unit_code character varying(50) NOT NULL,
        denominator_unit_code character varying(50) NOT NULL,
        geography text NOT NULL,
        valid_from date,
        valid_to date,
        publication_status character varying(30) NOT NULL,
        source_dataset_version text NOT NULL,
        license_code text NOT NULL,
        supersedes_version_id uuid,
        CONSTRAINT pk_emission_factor_versions PRIMARY KEY (id),
        CONSTRAINT fk_emission_factor_versions_emission_factor_versions_supersede FOREIGN KEY (supersedes_version_id) REFERENCES app.emission_factor_versions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.organizations (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_organizations PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE identity.roles (
        id uuid NOT NULL,
        name character varying(256),
        normalized_name character varying(256),
        concurrency_stamp text,
        CONSTRAINT pk_roles PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.units (
        id uuid NOT NULL,
        code text NOT NULL,
        symbol text NOT NULL,
        dimension text NOT NULL,
        scale_to_canonical numeric(30,15) NOT NULL,
        offset_to_canonical numeric(30,15) NOT NULL,
        canonical_code text NOT NULL,
        catalogue_version text NOT NULL,
        CONSTRAINT pk_units PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE identity.users (
        id uuid NOT NULL,
        display_name text NOT NULL,
        user_name character varying(256),
        normalized_user_name character varying(256),
        email character varying(256),
        normalized_email character varying(256),
        email_confirmed boolean NOT NULL,
        password_hash text,
        security_stamp text,
        concurrency_stamp text,
        phone_number text,
        phone_number_confirmed boolean NOT NULL,
        two_factor_enabled boolean NOT NULL,
        lockout_end timestamp with time zone,
        lockout_enabled boolean NOT NULL,
        access_failed_count integer NOT NULL,
        CONSTRAINT pk_users PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.products (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        name character varying(300) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_products PRIMARY KEY (id),
        CONSTRAINT fk_products_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES app.organizations (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE identity.role_claims (
        id integer GENERATED BY DEFAULT AS IDENTITY,
        role_id uuid NOT NULL,
        claim_type text,
        claim_value text,
        CONSTRAINT pk_role_claims PRIMARY KEY (id),
        CONSTRAINT fk_role_claims_roles_role_id FOREIGN KEY (role_id) REFERENCES identity.roles (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.organization_memberships (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        user_id uuid NOT NULL,
        role character varying(50) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        CONSTRAINT pk_organization_memberships PRIMARY KEY (id),
        CONSTRAINT fk_organization_memberships_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES app.organizations (id) ON DELETE RESTRICT,
        CONSTRAINT fk_organization_memberships_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE identity.user_claims (
        id integer GENERATED BY DEFAULT AS IDENTITY,
        user_id uuid NOT NULL,
        claim_type text,
        claim_value text,
        CONSTRAINT pk_user_claims PRIMARY KEY (id),
        CONSTRAINT fk_user_claims_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE identity.user_logins (
        login_provider character varying(128) NOT NULL,
        provider_key character varying(128) NOT NULL,
        provider_display_name text,
        user_id uuid NOT NULL,
        CONSTRAINT pk_user_logins PRIMARY KEY (login_provider, provider_key),
        CONSTRAINT fk_user_logins_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE identity.user_roles (
        user_id uuid NOT NULL,
        role_id uuid NOT NULL,
        CONSTRAINT pk_user_roles PRIMARY KEY (user_id, role_id),
        CONSTRAINT fk_user_roles_roles_role_id FOREIGN KEY (role_id) REFERENCES identity.roles (id) ON DELETE CASCADE,
        CONSTRAINT fk_user_roles_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE identity.user_tokens (
        user_id uuid NOT NULL,
        login_provider character varying(128) NOT NULL,
        name character varying(128) NOT NULL,
        value text,
        CONSTRAINT pk_user_tokens PRIMARY KEY (user_id, login_provider, name),
        CONSTRAINT fk_user_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.product_versions (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        product_id uuid NOT NULL,
        version_number integer NOT NULL,
        name_zh_tw character varying(300) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_product_versions PRIMARY KEY (id),
        CONSTRAINT fk_product_versions_products_product_id FOREIGN KEY (product_id) REFERENCES app.products (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.inventory_project_versions (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        product_version_id uuid NOT NULL,
        version_number integer NOT NULL,
        period_start date NOT NULL,
        period_end date NOT NULL,
        functional_unit character varying(200) NOT NULL,
        pcr_version character varying(200) NOT NULL,
        workflow_status character varying(50) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_inventory_project_versions PRIMARY KEY (id),
        CONSTRAINT fk_inventory_project_versions_product_versions_product_version FOREIGN KEY (product_version_id) REFERENCES app.product_versions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.activity_data_versions (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        inventory_project_version_id uuid NOT NULL,
        lifecycle_stage integer NOT NULL,
        name character varying(300) NOT NULL,
        raw_value numeric(30,12) NOT NULL,
        raw_unit_code character varying(50) NOT NULL,
        canonical_value numeric(30,12) NOT NULL,
        canonical_unit_code character varying(50) NOT NULL,
        conversion_rule_version character varying(100) NOT NULL,
        period_start date NOT NULL,
        period_end date NOT NULL,
        factor_version_id uuid NOT NULL,
        evidence_sha256 character varying(64),
        CONSTRAINT pk_activity_data_versions PRIMARY KEY (id),
        CONSTRAINT fk_activity_data_versions_emission_factor_versions_factor_vers FOREIGN KEY (factor_version_id) REFERENCES app.emission_factor_versions (id) ON DELETE RESTRICT,
        CONSTRAINT fk_activity_data_versions_inventory_project_versions_inventory FOREIGN KEY (inventory_project_version_id) REFERENCES app.inventory_project_versions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.calculation_runs (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        project_version_id uuid NOT NULL,
        supersedes_run_id uuid,
        canonical_input_manifest jsonb NOT NULL,
        input_sha256 character varying(64) NOT NULL,
        engine_build text NOT NULL,
        rule_set_version text NOT NULL,
        unit_catalogue_version text NOT NULL,
        gwp_version text NOT NULL,
        pcr_version text NOT NULL,
        product_total numeric(38,15) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_calculation_runs PRIMARY KEY (id),
        CONSTRAINT fk_calculation_runs_calculation_runs_supersedes_run_id FOREIGN KEY (supersedes_run_id) REFERENCES app.calculation_runs (id) ON DELETE RESTRICT,
        CONSTRAINT fk_calculation_runs_inventory_project_versions_project_version FOREIGN KEY (project_version_id) REFERENCES app.inventory_project_versions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.calculation_line_items (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        calculation_run_id uuid NOT NULL,
        activity_id uuid NOT NULL,
        lifecycle_stage integer NOT NULL,
        formula_id text NOT NULL,
        canonical_activity_value numeric(30,12) NOT NULL,
        activity_unit_code text NOT NULL,
        factor_version_id uuid NOT NULL,
        factor_value numeric(30,15) NOT NULL,
        factor_unit text NOT NULL,
        emissions numeric(38,15) NOT NULL,
        emissions_unit_code text NOT NULL,
        CONSTRAINT pk_calculation_line_items PRIMARY KEY (id),
        CONSTRAINT fk_calculation_line_items_calculation_runs_calculation_run_id FOREIGN KEY (calculation_run_id) REFERENCES app.calculation_runs (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE TABLE app.calculation_stage_summaries (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        calculation_run_id uuid NOT NULL,
        lifecycle_stage integer NOT NULL,
        emissions numeric(38,15) NOT NULL,
        CONSTRAINT pk_calculation_stage_summaries PRIMARY KEY (id),
        CONSTRAINT fk_calculation_stage_summaries_calculation_runs_calculation_ru FOREIGN KEY (calculation_run_id) REFERENCES app.calculation_runs (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_activity_data_versions_factor_version_id ON app.activity_data_versions (factor_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_activity_data_versions_inventory_project_version_id ON app.activity_data_versions (inventory_project_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_audit_events_organization_id_timestamp ON app.audit_events (organization_id, timestamp);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_calculation_line_items_calculation_run_id ON app.calculation_line_items (calculation_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_calculation_runs_organization_id_input_sha256 ON app.calculation_runs (organization_id, input_sha256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_calculation_runs_project_version_id ON app.calculation_runs (project_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_calculation_runs_supersedes_run_id ON app.calculation_runs (supersedes_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_calculation_stage_summaries_calculation_run_id_lifecycle_st ON app.calculation_stage_summaries (calculation_run_id, lifecycle_stage);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_emission_factor_versions_factor_id_version_number ON app.emission_factor_versions (factor_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_emission_factor_versions_supersedes_version_id ON app.emission_factor_versions (supersedes_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_inventory_project_versions_product_version_id_version_number ON app.inventory_project_versions (product_version_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_organization_memberships_organization_id_user_id ON app.organization_memberships (organization_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_organization_memberships_user_id ON app.organization_memberships (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_product_versions_product_id_version_number ON app.product_versions (product_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_products_organization_id ON app.products (organization_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_role_claims_role_id ON identity.role_claims (role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE UNIQUE INDEX "RoleNameIndex" ON identity.roles (normalized_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_units_code_catalogue_version ON app.units (code, catalogue_version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_user_claims_user_id ON identity.user_claims (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_user_logins_user_id ON identity.user_logins (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX ix_user_roles_role_id ON identity.user_roles (role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE INDEX "EmailIndex" ON identity.users (normalized_email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    CREATE UNIQUE INDEX "UserNameIndex" ON identity.users (normalized_user_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718081447_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718081447_InitialCreate', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718082504_SeedUnitCatalogueV1') THEN
    INSERT INTO app.units (id, canonical_code, catalogue_version, code, dimension, offset_to_canonical, scale_to_canonical, symbol)
    VALUES ('71000000-0000-0000-0000-000000000001', 'kg', 'units-p0-v1', 'kg', 'mass', 0.0, 1.0, 'kg');
    INSERT INTO app.units (id, canonical_code, catalogue_version, code, dimension, offset_to_canonical, scale_to_canonical, symbol)
    VALUES ('71000000-0000-0000-0000-000000000002', 'kg', 'units-p0-v1', 'g', 'mass', 0.0, 0.001, 'g');
    INSERT INTO app.units (id, canonical_code, catalogue_version, code, dimension, offset_to_canonical, scale_to_canonical, symbol)
    VALUES ('71000000-0000-0000-0000-000000000003', 'kWh', 'units-p0-v1', 'kWh', 'energy', 0.0, 1.0, 'kWh');
    INSERT INTO app.units (id, canonical_code, catalogue_version, code, dimension, offset_to_canonical, scale_to_canonical, symbol)
    VALUES ('71000000-0000-0000-0000-000000000004', 'tonne-km', 'units-p0-v1', 'tonne-km', 'transport-work', 0.0, 1.0, 't·km');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718082504_SeedUnitCatalogueV1') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718082504_SeedUnitCatalogueV1', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718085311_AddPcrGovernance') THEN
    ALTER TABLE app.inventory_project_versions ADD pcr_version_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718085311_AddPcrGovernance') THEN
    CREATE TABLE app.pcr_versions (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        registration_number character varying(100) NOT NULL,
        version_number integer NOT NULL,
        title character varying(300) NOT NULL,
        valid_from date,
        valid_to date,
        publication_status character varying(30) NOT NULL,
        source_reference character varying(500) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        published_at timestamp with time zone,
        withdrawn_at timestamp with time zone,
        CONSTRAINT pk_pcr_versions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718085311_AddPcrGovernance') THEN
    CREATE INDEX ix_inventory_project_versions_pcr_version_id ON app.inventory_project_versions (pcr_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718085311_AddPcrGovernance') THEN
    CREATE UNIQUE INDEX ix_pcr_versions_organization_id_registration_number_version_nu ON app.pcr_versions (organization_id, registration_number, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718085311_AddPcrGovernance') THEN
    ALTER TABLE app.inventory_project_versions ADD CONSTRAINT fk_inventory_project_versions_pcr_versions_pcr_version_id FOREIGN KEY (pcr_version_id) REFERENCES app.pcr_versions (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718085311_AddPcrGovernance') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718085311_AddPcrGovernance', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718090200_AddEvidenceStorage') THEN
    CREATE TABLE app.evidence_files (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        activity_data_id uuid NOT NULL,
        object_key character varying(500) NOT NULL,
        original_file_name character varying(300) NOT NULL,
        content_type character varying(200) NOT NULL,
        size_bytes bigint NOT NULL,
        sha256 character varying(64) NOT NULL,
        scan_status character varying(30) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_evidence_files PRIMARY KEY (id),
        CONSTRAINT fk_evidence_files_activity_data_versions_activity_data_id FOREIGN KEY (activity_data_id) REFERENCES app.activity_data_versions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718090200_AddEvidenceStorage') THEN
    CREATE INDEX ix_evidence_files_activity_data_id ON app.evidence_files (activity_data_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718090200_AddEvidenceStorage') THEN
    CREATE INDEX ix_evidence_files_organization_id_sha256 ON app.evidence_files (organization_id, sha256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718090200_AddEvidenceStorage') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718090200_AddEvidenceStorage', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718091011_AddCalculationWarnings') THEN
    CREATE TABLE app.calculation_warnings (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        calculation_run_id uuid NOT NULL,
        code character varying(100) NOT NULL,
        message character varying(1000) NOT NULL,
        CONSTRAINT pk_calculation_warnings PRIMARY KEY (id),
        CONSTRAINT fk_calculation_warnings_calculation_runs_calculation_run_id FOREIGN KEY (calculation_run_id) REFERENCES app.calculation_runs (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718091011_AddCalculationWarnings') THEN
    CREATE INDEX ix_calculation_warnings_calculation_run_id ON app.calculation_warnings (calculation_run_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718091011_AddCalculationWarnings') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718091011_AddCalculationWarnings', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718091534_AddInventoryReview') THEN
    ALTER TABLE app.inventory_project_versions ADD review_comment character varying(2000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718091534_AddInventoryReview') THEN
    ALTER TABLE app.inventory_project_versions ADD reviewed_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718091534_AddInventoryReview') THEN
    ALTER TABLE app.inventory_project_versions ADD reviewed_by uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718091534_AddInventoryReview') THEN
    ALTER TABLE app.inventory_project_versions ADD submitted_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718091534_AddInventoryReview') THEN
    CREATE INDEX ix_inventory_project_versions_organization_id_workflow_status ON app.inventory_project_versions (organization_id, workflow_status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718091534_AddInventoryReview') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718091534_AddInventoryReview', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718092341_AddLegacyStaging') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'staging') THEN
            CREATE SCHEMA staging;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718092341_AddLegacyStaging') THEN
    CREATE TABLE staging.import_batches (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        source_file_name character varying(300) NOT NULL,
        source_file_sha256 character varying(64) NOT NULL,
        entity_type character varying(100) NOT NULL,
        status character varying(30) NOT NULL,
        parsed_rows integer NOT NULL,
        invalid_rows integer NOT NULL,
        conflict_rows integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        completed_at timestamp with time zone,
        CONSTRAINT pk_import_batches PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718092341_AddLegacyStaging') THEN
    CREATE TABLE staging.rows (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        import_batch_id uuid NOT NULL,
        source_row_number bigint NOT NULL,
        raw_payload_json text NOT NULL,
        raw_sha256 character varying(64) NOT NULL,
        parse_status character varying(30) NOT NULL,
        validation_errors_json text,
        CONSTRAINT pk_rows PRIMARY KEY (id),
        CONSTRAINT fk_rows_import_batches_import_batch_id FOREIGN KEY (import_batch_id) REFERENCES staging.import_batches (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718092341_AddLegacyStaging') THEN
    CREATE TABLE staging.conflicts (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        import_batch_id uuid NOT NULL,
        staging_row_id uuid NOT NULL,
        conflict_key character varying(500) NOT NULL,
        details_json text NOT NULL,
        CONSTRAINT pk_conflicts PRIMARY KEY (id),
        CONSTRAINT fk_conflicts_import_batches_import_batch_id FOREIGN KEY (import_batch_id) REFERENCES staging.import_batches (id) ON DELETE RESTRICT,
        CONSTRAINT fk_conflicts_rows_staging_row_id FOREIGN KEY (staging_row_id) REFERENCES staging.rows (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718092341_AddLegacyStaging') THEN
    CREATE INDEX ix_conflicts_import_batch_id_conflict_key ON staging.conflicts (import_batch_id, conflict_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718092341_AddLegacyStaging') THEN
    CREATE INDEX ix_conflicts_staging_row_id ON staging.conflicts (staging_row_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718092341_AddLegacyStaging') THEN
    CREATE UNIQUE INDEX ix_import_batches_organization_id_source_file_sha256_entity_ty ON staging.import_batches (organization_id, source_file_sha256, entity_type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718092341_AddLegacyStaging') THEN
    CREATE UNIQUE INDEX ix_rows_import_batch_id_source_row_number ON staging.rows (import_batch_id, source_row_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718092341_AddLegacyStaging') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718092341_AddLegacyStaging', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.products ADD category_code character varying(100) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.products ADD facility_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.inventory_project_versions ADD allocation_method character varying(200) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.inventory_project_versions ADD allocation_reason character varying(2000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.inventory_project_versions ADD assumptions character varying(4000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.inventory_project_versions ADD declared_unit character varying(200) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.inventory_project_versions ADD estimation_reason character varying(4000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.inventory_project_versions ADD exclusions character varying(4000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.inventory_project_versions ADD system_boundary character varying(1000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    CREATE TABLE app.facilities (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        code character varying(100) NOT NULL,
        name character varying(300) NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_facilities PRIMARY KEY (id),
        CONSTRAINT fk_facilities_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES app.organizations (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    CREATE TABLE app.organization_invitations (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        email character varying(320) NOT NULL,
        role character varying(50) NOT NULL,
        token_sha256 character varying(64) NOT NULL,
        invited_by uuid NOT NULL,
        created_at timestamp with time zone NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        accepted_at timestamp with time zone,
        revoked_at timestamp with time zone,
        CONSTRAINT pk_organization_invitations PRIMARY KEY (id),
        CONSTRAINT fk_organization_invitations_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES app.organizations (id) ON DELETE RESTRICT,
        CONSTRAINT fk_organization_invitations_users_invited_by FOREIGN KEY (invited_by) REFERENCES identity.users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    CREATE INDEX ix_products_facility_id ON app.products (facility_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    CREATE UNIQUE INDEX ix_facilities_organization_id_code ON app.facilities (organization_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    CREATE INDEX ix_organization_invitations_invited_by ON app.organization_invitations (invited_by);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    CREATE INDEX ix_organization_invitations_organization_id_email ON app.organization_invitations (organization_id, email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    CREATE UNIQUE INDEX ix_organization_invitations_token_sha256 ON app.organization_invitations (token_sha256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    ALTER TABLE app.products ADD CONSTRAINT fk_products_facilities_facility_id FOREIGN KEY (facility_id) REFERENCES app.facilities (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095107_AddOrganizationInventoryFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718095107_AddOrganizationInventoryFoundation', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.units ADD aliases_csv character varying(500) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.units ADD composite_expression character varying(200) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.pcr_versions ADD applicability character varying(2000) NOT NULL DEFAULT 'legacy-existing-version';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.pcr_versions ADD ccc_classification character varying(100) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.pcr_versions ADD original_document_name character varying(300) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.pcr_versions ADD original_document_sha256 character varying(64) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.pcr_versions ADD review_status character varying(30) NOT NULL DEFAULT 'Approved';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.pcr_versions ADD reviewed_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.pcr_versions ADD reviewed_by uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.pcr_versions ADD rule_requirements character varying(4000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.pcr_versions ADD standard_code character varying(100) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.emission_factor_versions ADD applicability character varying(2000) NOT NULL DEFAULT 'legacy-existing-version';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.emission_factor_versions ADD dataset_name character varying(300) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.emission_factor_versions ADD published_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.emission_factor_versions ADD review_status character varying(30) NOT NULL DEFAULT 'Approved';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.emission_factor_versions ADD reviewed_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.emission_factor_versions ADD reviewed_by uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.emission_factor_versions ADD source_name character varying(300) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    ALTER TABLE app.emission_factor_versions ADD withdrawn_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    UPDATE app.units SET aliases_csv = 'kilogram,kilograms', composite_expression = ''
    WHERE id = '71000000-0000-0000-0000-000000000001';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    UPDATE app.units SET aliases_csv = 'gram,grams', composite_expression = ''
    WHERE id = '71000000-0000-0000-0000-000000000002';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    UPDATE app.units SET aliases_csv = 'kilowatt-hour', composite_expression = ''
    WHERE id = '71000000-0000-0000-0000-000000000003';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    UPDATE app.units SET aliases_csv = 't-km,tkm', composite_expression = 'tonne*km'
    WHERE id = '71000000-0000-0000-0000-000000000004';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718095913_AddGovernanceMetadata') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718095913_AddGovernanceMetadata', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    ALTER TABLE app.calculation_line_items ADD allocation_factor numeric(18,15) NOT NULL DEFAULT 1.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    ALTER TABLE app.activity_data_versions ADD activity_kind character varying(100) NOT NULL DEFAULT 'Material';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    ALTER TABLE app.activity_data_versions ADD allocation_factor numeric(18,15) NOT NULL DEFAULT 1.0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    ALTER TABLE app.activity_data_versions ADD data_quality character varying(100) NOT NULL DEFAULT 'legacy-existing';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    ALTER TABLE app.activity_data_versions ADD estimation_reason character varying(4000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    ALTER TABLE app.activity_data_versions ADD is_estimated boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    ALTER TABLE app.activity_data_versions ADD supplier_or_scenario character varying(1000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    CREATE TABLE app.lifecycle_stage_declarations (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        inventory_project_version_id uuid NOT NULL,
        lifecycle_stage integer NOT NULL,
        is_applicable boolean NOT NULL,
        reason character varying(2000) NOT NULL,
        CONSTRAINT pk_lifecycle_stage_declarations PRIMARY KEY (id),
        CONSTRAINT fk_lifecycle_stage_declarations_inventory_project_versions_inv FOREIGN KEY (inventory_project_version_id) REFERENCES app.inventory_project_versions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    CREATE UNIQUE INDEX ix_lifecycle_stage_declarations_inventory_project_version_id_l ON app.lifecycle_stage_declarations (inventory_project_version_id, lifecycle_stage);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    UPDATE app.activity_data_versions
    SET activity_kind = CASE lifecycle_stage
        WHEN 1 THEN 'Material'
        WHEN 2 THEN 'Energy'
        WHEN 3 THEN 'DistributionTransport'
        WHEN 4 THEN 'UseEnergy'
        WHEN 5 THEN 'EndOfLifeTreatment'
        END;

    INSERT INTO app.lifecycle_stage_declarations
        (id, organization_id, inventory_project_version_id, lifecycle_stage, is_applicable, reason)
    SELECT gen_random_uuid(), inventory.organization_id, inventory.id, stage.value, TRUE, ''
    FROM app.inventory_project_versions AS inventory
    CROSS JOIN generate_series(1, 5) AS stage(value);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260718100827_AddLifecycleActivityGovernance') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260718100827_AddLifecycleActivityGovernance', '10.0.10');
    END IF;
END $EF$;
COMMIT;

