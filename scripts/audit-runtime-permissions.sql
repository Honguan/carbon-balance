-- Run as the migration/administration role after migrations:
-- psql ... -v ON_ERROR_STOP=1 -v runtime_role=carbon_runtime -f scripts/audit-runtime-permissions.sql
-- Provision the runtime LOGIN and its business-table privileges separately; do not store passwords here.
BEGIN;
SELECT set_config('carbon.audit_runtime_role', :'runtime_role', true);
DO $$
DECLARE runtime_name text := current_setting('carbon.audit_runtime_role'); runtime_oid oid;
BEGIN
    SELECT oid INTO runtime_oid FROM pg_roles WHERE rolname = runtime_name;
    IF runtime_oid IS NULL THEN RAISE EXCEPTION 'Runtime role does not exist'; END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE oid = runtime_oid AND
        (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls))
        OR EXISTS (SELECT 1 FROM pg_database WHERE datname = current_database()
            AND pg_has_role(runtime_oid, datdba, 'MEMBER'))
        OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspname IN ('app', 'identity')
            AND pg_has_role(runtime_oid, nspowner, 'MEMBER'))
        OR EXISTS (SELECT 1 FROM pg_class WHERE oid IN ('app.audit_events'::regclass, 'identity.system_audit_events'::regclass)
            AND pg_has_role(runtime_oid, relowner, 'MEMBER'))
        OR EXISTS (SELECT 1 FROM pg_proc WHERE oid = 'app.reject_audit_mutation()'::regprocedure
            AND pg_has_role(runtime_oid, proowner, 'MEMBER')) THEN
        RAISE EXCEPTION 'Runtime must be a non-owner role without administrative capabilities or owner membership';
    END IF;
    EXECUTE format('GRANT USAGE ON SCHEMA app, identity TO %I', runtime_name);
    EXECUTE format('REVOKE CREATE ON SCHEMA app, identity FROM %I', runtime_name);
    EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER ON app.audit_events, identity.system_audit_events FROM %I', runtime_name);
    EXECUTE format('GRANT SELECT, INSERT ON app.audit_events, identity.system_audit_events TO %I', runtime_name);
    IF has_schema_privilege(runtime_oid, 'app', 'CREATE') OR has_schema_privilege(runtime_oid, 'identity', 'CREATE')
        OR EXISTS (SELECT 1 FROM unnest(ARRAY['app.audit_events', 'identity.system_audit_events']) AS tables(name)
            WHERE has_table_privilege(runtime_oid, name, 'UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')) THEN
        RAISE EXCEPTION 'Inherited or PUBLIC privileges violate the audit runtime contract; fix grants before deployment';
    END IF;
END $$;
COMMIT;
