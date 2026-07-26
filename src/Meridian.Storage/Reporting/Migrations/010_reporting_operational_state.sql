create table if not exists __SCHEMA__.reporting_run_snapshots (
    tenant_id text not null,
    run_id text not null,
    run_id_key text not null,
    manifest_payload jsonb not null,
    audit_payload jsonb not null,
    updated_at_utc timestamptz not null,
    certified_dataset_hash_sha256 text not null,
    manifest_hash_sha256 text not null,
    audit_hash_sha256 text not null,
    state_hash_sha256 text not null,
    primary key (tenant_id, run_id_key),
    constraint ck_reporting_run_snapshot_tenant
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_run_snapshot_id
        check (run_id = btrim(run_id) and length(run_id) between 1 and 256),
    constraint ck_reporting_run_snapshot_key
        check (run_id_key = lower(run_id)),
    constraint ck_reporting_run_snapshot_manifest
        check (jsonb_typeof(manifest_payload) = 'object'),
    constraint ck_reporting_run_snapshot_audit
        check (jsonb_typeof(audit_payload) = 'array'),
    constraint ck_reporting_run_snapshot_payload_identity
        check ((manifest_payload ->> 'runId') = run_id
            and (manifest_payload #>> '{operationalScope,tenantId}') = tenant_id),
    constraint ck_reporting_run_snapshot_dataset_hash
        check (certified_dataset_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_run_snapshot_manifest_hash
        check (manifest_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_run_snapshot_audit_hash
        check (audit_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_run_snapshot_state_hash
        check (state_hash_sha256 ~ '^[0-9a-f]{64}$')
);

create index if not exists ix_reporting_run_snapshots_recent
    on __SCHEMA__.reporting_run_snapshots (updated_at_utc desc, tenant_id, run_id_key);

create table if not exists __SCHEMA__.reporting_schedule_snapshots (
    tenant_id text not null,
    company_id text not null,
    schedule_id text not null,
    schedule_id_key text not null,
    schedule_payload jsonb not null,
    payload_hash_sha256 text not null,
    stored_at_utc timestamptz not null,
    primary key (tenant_id, company_id, schedule_id_key),
    constraint ck_reporting_schedule_snapshot_tenant
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_schedule_snapshot_company
        check (company_id = btrim(company_id) and length(company_id) between 1 and 256),
    constraint ck_reporting_schedule_snapshot_id
        check (schedule_id = btrim(schedule_id) and length(schedule_id) between 1 and 256),
    constraint ck_reporting_schedule_snapshot_key
        check (schedule_id_key = lower(schedule_id)),
    constraint ck_reporting_schedule_snapshot_payload
        check (jsonb_typeof(schedule_payload) = 'object'),
    constraint ck_reporting_schedule_snapshot_payload_identity
        check ((schedule_payload ->> 'tenantId') = tenant_id
            and (schedule_payload ->> 'companyId') = company_id
            and (schedule_payload ->> 'scheduleId') = schedule_id),
    constraint ck_reporting_schedule_snapshot_payload_hash
        check (payload_hash_sha256 ~ '^[0-9a-f]{64}$')
);

create index if not exists ix_reporting_schedule_snapshots_due
    on __SCHEMA__.reporting_schedule_snapshots (
        tenant_id,
        company_id,
        ((schedule_payload ->> 'dueAtUtc')::timestamptz),
        schedule_id_key);
