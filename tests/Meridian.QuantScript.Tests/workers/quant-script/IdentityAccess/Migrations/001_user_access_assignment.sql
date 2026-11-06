-- Scoped access assignments (identity governance).
-- Formerly applied as inline DDL by PostgresScopedAccessAssignmentStore.EnsureMigratedAsync;
-- the statements keep their IF NOT EXISTS guards because this script historically re-ran on
-- every startup and the runner's Reapply drift policy preserves that edit-in-place workflow.
CREATE TABLE IF NOT EXISTS __SCHEMA__."user_access_assignment" (
    assignment_id uuid PRIMARY KEY,
    principal_id text NOT NULL,
    principal_kind text NOT NULL,
    scope_kind text NOT NULL,
    scope_id uuid NULL,
    role text NOT NULL,
    role_profile_name text NULL,
    permission_names jsonb NOT NULL,
    permission_mask bigint NOT NULL,
    effective_from timestamptz NOT NULL,
    effective_to timestamptz NULL,
    granted_by text NOT NULL,
    rationale text NOT NULL,
    correlation_id text NOT NULL,
    version bigint NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    revoked_by text NULL,
    revoked_at_utc timestamptz NULL,
    revocation_reason text NULL,
    last_audit_id text NULL,
    approval_limit_amount numeric NULL,
    approval_limit_currency text NULL,
    segregation_of_duties_rule text NULL
);
ALTER TABLE __SCHEMA__."user_access_assignment"
    ADD COLUMN IF NOT EXISTS approval_limit_amount numeric NULL;
ALTER TABLE __SCHEMA__."user_access_assignment"
    ADD COLUMN IF NOT EXISTS approval_limit_currency text NULL;
ALTER TABLE __SCHEMA__."user_access_assignment"
    ADD COLUMN IF NOT EXISTS segregation_of_duties_rule text NULL;
CREATE INDEX IF NOT EXISTS ix_user_access_assignment_principal
    ON __SCHEMA__."user_access_assignment" (principal_id, scope_kind, scope_id);
