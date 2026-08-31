create table if not exists __SCHEMA__.reporting_access_grants (
    grant_id text primary key,
    token_hash_sha256 text not null,
    tenant_id text not null,
    audience text not null,
    package_id text not null,
    allow_package_read boolean not null,
    artifact_ids text[] not null default '{}',
    created_at_utc timestamptz not null,
    expires_at_utc timestamptz not null,
    max_uses integer not null,
    use_count integer not null default 0,
    last_used_at_utc timestamptz null,
    revoked_at_utc timestamptz null,
    revoked_by text null,
    revocation_reason text null,
    version bigint not null default 0,
    constraint ck_reporting_access_grant_id_trimmed
        check (grant_id = btrim(grant_id) and length(grant_id) between 1 and 256),
    constraint ck_reporting_access_grant_token_hash
        check (token_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_access_grant_tenant_trimmed
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_access_grant_audience_trimmed
        check (audience = btrim(audience) and length(audience) between 1 and 512),
    constraint ck_reporting_access_grant_package_trimmed
        check (package_id = btrim(package_id) and length(package_id) between 1 and 256),
    constraint ck_reporting_access_grant_artifacts_valid
        check (array_position(artifact_ids, null) is null),
    constraint ck_reporting_access_grant_window
        check (expires_at_utc > created_at_utc),
    constraint ck_reporting_access_grant_uses
        check (max_uses > 0 and use_count between 0 and max_uses),
    constraint ck_reporting_access_grant_last_use
        check (last_used_at_utc is null or last_used_at_utc >= created_at_utc),
    constraint ck_reporting_access_grant_revocation
        check ((revoked_at_utc is null and revoked_by is null and revocation_reason is null)
            or (revoked_at_utc is not null
                and revoked_at_utc >= created_at_utc
                and revoked_by = btrim(revoked_by)
                and length(revoked_by) between 1 and 256
                and revocation_reason = btrim(revocation_reason)
                and length(revocation_reason) between 1 and 2048)),
    constraint ck_reporting_access_grant_version
        check (version >= 0)
);

create index if not exists ix_reporting_access_grants_tenant_package
    on __SCHEMA__.reporting_access_grants (tenant_id, package_id, expires_at_utc desc);

create index if not exists ix_reporting_access_grants_expiration
    on __SCHEMA__.reporting_access_grants (expires_at_utc)
    where revoked_at_utc is null;

create or replace function __SCHEMA__.guard_reporting_access_grant_mutation()
returns trigger
language plpgsql
as $$
begin
    if tg_op = 'DELETE' then
        raise exception 'reporting access grants cannot be deleted'
            using errcode = '55000';
    end if;

    if new.grant_id is distinct from old.grant_id
        or new.token_hash_sha256 is distinct from old.token_hash_sha256
        or new.tenant_id is distinct from old.tenant_id
        or new.audience is distinct from old.audience
        or new.package_id is distinct from old.package_id
        or new.allow_package_read is distinct from old.allow_package_read
        or new.artifact_ids is distinct from old.artifact_ids
        or new.created_at_utc is distinct from old.created_at_utc
        or new.expires_at_utc is distinct from old.expires_at_utc
        or new.max_uses is distinct from old.max_uses then
        raise exception 'reporting access grant authority scope is immutable'
            using errcode = '55000';
    end if;

    if new.version <> old.version + 1
        or new.use_count < old.use_count
        or (old.last_used_at_utc is not null
            and (new.last_used_at_utc is null
                or new.last_used_at_utc < old.last_used_at_utc))
        or (old.revoked_at_utc is not null
            and (new.revoked_at_utc is distinct from old.revoked_at_utc
                or new.revoked_by is distinct from old.revoked_by
                or new.revocation_reason is distinct from old.revocation_reason)) then
        raise exception 'invalid reporting access grant state transition'
            using errcode = '55000';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_reporting_access_grants_guard
    on __SCHEMA__.reporting_access_grants;

create trigger trg_reporting_access_grants_guard
before update or delete on __SCHEMA__.reporting_access_grants
for each row execute function __SCHEMA__.guard_reporting_access_grant_mutation();

create table if not exists __SCHEMA__.reporting_delivery_jobs (
    job_id text primary key,
    tenant_id text not null,
    package_id text not null,
    distribution_id text not null,
    transport_id text not null,
    release_authorization jsonb not null,
    requested_by text not null,
    idempotency_key text not null unique,
    payload jsonb not null,
    state integer not null,
    attempt_count integer not null,
    max_attempts integer not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    next_attempt_at_utc timestamptz null,
    lease_owner text null,
    lease_expires_at_utc timestamptz null,
    last_error_code text null,
    last_error text null,
    provider_message_id text null,
    access_grant_id text null,
    version bigint not null default 0,
    constraint uq_reporting_delivery_job_tenant unique (job_id, tenant_id),
    constraint ck_reporting_delivery_job_id_trimmed
        check (job_id = btrim(job_id) and length(job_id) between 1 and 256),
    constraint ck_reporting_delivery_job_tenant_trimmed
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_delivery_job_package_trimmed
        check (package_id = btrim(package_id) and length(package_id) between 1 and 256),
    constraint ck_reporting_delivery_job_distribution_trimmed
        check (distribution_id = btrim(distribution_id) and length(distribution_id) between 1 and 256),
    constraint ck_reporting_delivery_job_transport_trimmed
        check (transport_id = btrim(transport_id) and length(transport_id) between 1 and 256),
    constraint ck_reporting_delivery_job_requester_trimmed
        check (requested_by = btrim(requested_by) and length(requested_by) between 1 and 256),
    constraint ck_reporting_delivery_job_idempotency
        check (idempotency_key ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_delivery_job_state
        check (state between 0 and 6),
    constraint ck_reporting_delivery_job_attempts
        check (max_attempts between 1 and 100 and attempt_count between 0 and max_attempts),
    constraint ck_reporting_delivery_job_time_order
        check (updated_at_utc >= created_at_utc),
    constraint ck_reporting_delivery_job_lease
        check ((state = 1 and lease_owner is not null and length(btrim(lease_owner)) between 1 and 256 and lease_expires_at_utc is not null)
            or (state <> 1 and lease_owner is null and lease_expires_at_utc is null)),
    constraint ck_reporting_delivery_job_next_attempt
        check ((state in (0, 2) and next_attempt_at_utc is not null)
            or (state not in (0, 2) and next_attempt_at_utc is null)),
    constraint ck_reporting_delivery_job_version
        check (version >= 0)
);

create index if not exists ix_reporting_delivery_jobs_tenant_package
    on __SCHEMA__.reporting_delivery_jobs (tenant_id, package_id, created_at_utc desc);

create index if not exists ix_reporting_delivery_jobs_due
    on __SCHEMA__.reporting_delivery_jobs (state, next_attempt_at_utc, lease_expires_at_utc, created_at_utc)
    where state in (0, 1, 2);

create or replace function __SCHEMA__.guard_reporting_delivery_job_mutation()
returns trigger
language plpgsql
as $$
begin
    if tg_op = 'DELETE' then
        raise exception 'reporting delivery jobs cannot be deleted'
            using errcode = '55000';
    end if;

    if new.job_id is distinct from old.job_id
        or new.tenant_id is distinct from old.tenant_id
        or new.package_id is distinct from old.package_id
        or new.distribution_id is distinct from old.distribution_id
        or new.transport_id is distinct from old.transport_id
        or new.release_authorization is distinct from old.release_authorization
        or new.requested_by is distinct from old.requested_by
        or new.idempotency_key is distinct from old.idempotency_key
        or new.payload is distinct from old.payload
        or new.max_attempts is distinct from old.max_attempts
        or new.created_at_utc is distinct from old.created_at_utc then
        raise exception 'reporting delivery authority and payload are immutable'
            using errcode = '55000';
    end if;

    if new.version <> old.version + 1
        or new.attempt_count < old.attempt_count
        or new.updated_at_utc < old.updated_at_utc then
        raise exception 'invalid reporting delivery state transition'
            using errcode = '55000';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_reporting_delivery_jobs_guard
    on __SCHEMA__.reporting_delivery_jobs;

create trigger trg_reporting_delivery_jobs_guard
before update or delete on __SCHEMA__.reporting_delivery_jobs
for each row execute function __SCHEMA__.guard_reporting_delivery_job_mutation();

create table if not exists __SCHEMA__.reporting_delivery_receipts (
    receipt_sequence bigint generated always as identity,
    job_id text not null,
    tenant_id text not null,
    receipt_id text not null,
    kind integer not null,
    occurred_at_utc timestamptz not null,
    transport_id text not null,
    provider_reference text null,
    evidence_reference text null,
    detail text null,
    stored_at_utc timestamptz not null default now(),
    primary key (job_id, receipt_id),
    constraint fk_reporting_delivery_receipt_job
        foreign key (job_id, tenant_id)
        references __SCHEMA__.reporting_delivery_jobs (job_id, tenant_id),
    constraint ck_reporting_delivery_receipt_tenant_trimmed
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_delivery_receipt_id_trimmed
        check (receipt_id = btrim(receipt_id) and length(receipt_id) between 1 and 256),
    constraint ck_reporting_delivery_receipt_kind
        check (kind between 0 and 7),
    constraint ck_reporting_delivery_receipt_transport
        check (transport_id = btrim(transport_id) and length(transport_id) between 1 and 256)
);

create index if not exists ix_reporting_delivery_receipts_tenant_job_sequence
    on __SCHEMA__.reporting_delivery_receipts (tenant_id, job_id, receipt_sequence);

create or replace function __SCHEMA__.reject_reporting_delivery_receipt_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception 'reporting delivery receipts are immutable'
        using errcode = '55000';
end;
$$;

drop trigger if exists trg_reporting_delivery_receipts_immutable
    on __SCHEMA__.reporting_delivery_receipts;

create trigger trg_reporting_delivery_receipts_immutable
before update or delete on __SCHEMA__.reporting_delivery_receipts
for each row execute function __SCHEMA__.reject_reporting_delivery_receipt_mutation();
