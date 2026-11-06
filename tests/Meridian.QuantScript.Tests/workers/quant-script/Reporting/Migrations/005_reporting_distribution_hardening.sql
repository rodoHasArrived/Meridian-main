alter table __SCHEMA__.reporting_access_grants
    add column if not exists run_id text;

-- The pre-hardening implementation used run_id as package_id, so this is the only safe legacy
-- backfill. New grants retain the content-derived package id and governed run id independently.
update __SCHEMA__.reporting_access_grants
set run_id = package_id
where run_id is null;

alter table __SCHEMA__.reporting_access_grants
    alter column run_id set not null;

-- Legacy grants were untyped user recipients. New grants retain the immutable principal namespace
-- so equal user/group/company identifiers cannot gain authority after restart hydration.
alter table __SCHEMA__.reporting_access_grants
    add column if not exists audience_kind integer not null default 0;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_reporting_access_grant_audience_kind'
          and conrelid = '__SCHEMA__.reporting_access_grants'::regclass) then
        alter table __SCHEMA__.reporting_access_grants
            add constraint ck_reporting_access_grant_audience_kind
                check (audience_kind between 0 and 2);
    end if;
end;
$$;

-- Older retained delivery authorizations predate the separate governed run identity. Preserve
-- restart readability by adding the former package id as runId; old jobs used run id as package id.
update __SCHEMA__.reporting_delivery_jobs
set release_authorization = jsonb_set(
    release_authorization,
    '{runId}',
    to_jsonb(release_authorization ->> 'packageId'),
    true)
where not (release_authorization ? 'runId')
  and coalesce(release_authorization ->> 'packageId', '') <> '';

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_reporting_access_grant_run_trimmed'
          and conrelid = '__SCHEMA__.reporting_access_grants'::regclass) then
        alter table __SCHEMA__.reporting_access_grants
            add constraint ck_reporting_access_grant_run_trimmed
                check (run_id = btrim(run_id) and length(run_id) between 1 and 256);
    end if;
end;
$$;

create index if not exists ix_reporting_access_grants_tenant_run_package
    on __SCHEMA__.reporting_access_grants (tenant_id, run_id, package_id, expires_at_utc desc);

-- A grant use is authorized against one captured instant. Fail migration closed if legacy data
-- claims a use at or after expiry rather than silently rewriting immutable access evidence.
alter table __SCHEMA__.reporting_access_grants
    drop constraint if exists ck_reporting_access_grant_last_use;

alter table __SCHEMA__.reporting_access_grants
    add constraint ck_reporting_access_grant_last_use
        check (last_used_at_utc is null
            or (last_used_at_utc >= created_at_utc
                and last_used_at_utc < expires_at_utc));

create unique index if not exists uq_reporting_delivery_jobs_access_grant
    on __SCHEMA__.reporting_delivery_jobs (access_grant_id)
    where access_grant_id is not null;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_reporting_delivery_provider_message_id'
          and conrelid = '__SCHEMA__.reporting_delivery_jobs'::regclass) then
        alter table __SCHEMA__.reporting_delivery_jobs
            add constraint ck_reporting_delivery_provider_message_id
                check (provider_message_id is null
                    or (provider_message_id = btrim(provider_message_id)
                        and length(provider_message_id) between 1 and 256));
    end if;

    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_reporting_delivery_receipt_provider_reference'
          and conrelid = '__SCHEMA__.reporting_delivery_receipts'::regclass) then
        alter table __SCHEMA__.reporting_delivery_receipts
            add constraint ck_reporting_delivery_receipt_provider_reference
                check (provider_reference is null
                    or (provider_reference = btrim(provider_reference)
                        and length(provider_reference) between 1 and 256));
    end if;
end;
$$;

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
        or new.audience_kind is distinct from old.audience_kind
        or new.run_id is distinct from old.run_id
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
        or new.use_count > old.use_count + 1
        or (new.use_count = old.use_count
            and new.last_used_at_utc is distinct from old.last_used_at_utc)
        or (new.use_count = old.use_count + 1
            and (new.last_used_at_utc is null
                or new.last_used_at_utc < new.created_at_utc))
        or (old.revoked_at_utc is not null
            and new.use_count <> old.use_count)
        or (old.revoked_at_utc is null
            and new.revoked_at_utc is not null
            and new.use_count <> old.use_count)
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
        or new.attempt_count > old.attempt_count + 1
        or new.updated_at_utc < old.updated_at_utc
        or (old.state = 1 and new.state = 1
            and not (
                (old.access_grant_id is null
                    and new.access_grant_id is not null
                    and new.attempt_count = old.attempt_count
                    and new.next_attempt_at_utc is not distinct from old.next_attempt_at_utc
                    and new.lease_owner is not distinct from old.lease_owner
                    and new.lease_expires_at_utc is not distinct from old.lease_expires_at_utc
                    and new.last_error_code is not distinct from old.last_error_code
                    and new.last_error is not distinct from old.last_error
                    and new.provider_message_id is not distinct from old.provider_message_id)
                or (old.lease_expires_at_utc is not null
                    and old.lease_expires_at_utc <= new.updated_at_utc
                    and new.lease_owner is not null
                    and btrim(new.lease_owner) <> ''
                    and new.lease_expires_at_utc > new.updated_at_utc
                    and new.attempt_count = old.attempt_count
                    and new.next_attempt_at_utc is not distinct from old.next_attempt_at_utc
                    and new.last_error_code is not distinct from old.last_error_code
                    and new.last_error is not distinct from old.last_error
                    and new.provider_message_id is not distinct from old.provider_message_id
                    and new.access_grant_id is not distinct from old.access_grant_id)))
        or (old.provider_message_id is not null
            and new.provider_message_id is distinct from old.provider_message_id)
        or (old.access_grant_id is not null
            and new.access_grant_id is distinct from old.access_grant_id) then
        raise exception 'invalid reporting delivery state transition'
            using errcode = '55000';
    end if;

    if not (
        (old.state = 0 and new.state = 1)
        or (old.state = 1 and new.state in (1, 2, 3, 4, 5, 6))
        or (old.state = 2 and (
            new.state = 1
            or (new.state in (3, 4, 6)
                and old.access_grant_id is not null
                and old.provider_message_id is null
                and old.last_error_code in ('RELAY_OUTCOME_UNKNOWN', 'TRANSPORT_CANCELLED')
                and new.provider_message_id is not null
                and new.access_grant_id is not distinct from old.access_grant_id
                and new.attempt_count = old.attempt_count
                and new.next_attempt_at_utc is null
                and new.lease_owner is null
                and new.lease_expires_at_utc is null)))
        or (old.state = 3 and new.state in (3, 4, 6))
        or (old.state = 4 and new.state = 4)
        or (old.state = 5 and (
            new.state = 5
            or (new.state in (3, 4, 6)
                and old.access_grant_id is not null
                and old.provider_message_id is null
                and old.last_error_code in ('RELAY_OUTCOME_UNKNOWN', 'TRANSPORT_CANCELLED')
                and new.provider_message_id is not null
                and new.access_grant_id is not distinct from old.access_grant_id
                and new.attempt_count = old.attempt_count
                and new.next_attempt_at_utc is null
                and new.lease_owner is null
                and new.lease_expires_at_utc is null)))
        or (old.state = 6 and new.state = 6)) then
        raise exception 'reporting delivery state cannot move backwards or bypass dispatch'
            using errcode = '55000';
    end if;

    return new;
end;
$$;

alter table __SCHEMA__.reporting_delivery_receipts
    drop constraint if exists ck_reporting_delivery_receipt_kind;

alter table __SCHEMA__.reporting_delivery_receipts
    add constraint ck_reporting_delivery_receipt_kind
        check (kind between 0 and 8);
