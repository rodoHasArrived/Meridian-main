create table if not exists __SCHEMA__.reporting_governed_runs (
    tenant_id text not null,
    run_id text not null,
    series_id text not null,
    revision integer not null,
    organization_id text not null,
    company_id text null,
    aggregate_version bigint not null,
    execution_state smallint not null,
    governance_state smallint not null,
    state_payload text not null,
    state_hash_sha256 text not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    primary key (tenant_id, run_id),
    unique (tenant_id, series_id, revision),
    constraint ck_reporting_governed_run_tenant
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_governed_run_id
        check (run_id = btrim(run_id) and length(run_id) between 1 and 256),
    constraint ck_reporting_governed_series_id
        check (series_id = btrim(series_id) and length(series_id) between 1 and 256),
    constraint ck_reporting_governed_revision check (revision > 0),
    constraint ck_reporting_governed_version check (aggregate_version > 0),
    constraint ck_reporting_governed_execution_state check (execution_state between 0 and 4),
    constraint ck_reporting_governed_governance_state check (governance_state between 0 and 4),
    constraint ck_reporting_governed_state_payload check (jsonb_typeof(state_payload::jsonb) = 'object'),
    constraint ck_reporting_governed_state_hash check (state_hash_sha256 ~ '^[0-9a-f]{64}$')
);

create index if not exists ix_reporting_governed_runs_series
    on __SCHEMA__.reporting_governed_runs (tenant_id, series_id, revision);

create index if not exists ix_reporting_governed_runs_scope
    on __SCHEMA__.reporting_governed_runs (tenant_id, organization_id, company_id, created_at_utc desc);

create table if not exists __SCHEMA__.reporting_restatement_requests (
    tenant_id text not null,
    request_id text not null,
    series_id text not null,
    predecessor_run_id text not null,
    aggregate_version bigint not null,
    request_state smallint not null,
    state_payload text not null,
    state_hash_sha256 text not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    primary key (tenant_id, request_id),
    constraint fk_reporting_restatement_predecessor
        foreign key (tenant_id, predecessor_run_id)
        references __SCHEMA__.reporting_governed_runs (tenant_id, run_id),
    constraint ck_reporting_restatement_tenant
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_restatement_request_id
        check (request_id = btrim(request_id) and length(request_id) between 1 and 256),
    constraint ck_reporting_restatement_series_id
        check (series_id = btrim(series_id) and length(series_id) between 1 and 256),
    constraint ck_reporting_restatement_version check (aggregate_version > 0),
    constraint ck_reporting_restatement_state check (request_state between 0 and 1),
    constraint ck_reporting_restatement_state_payload check (jsonb_typeof(state_payload::jsonb) = 'object'),
    constraint ck_reporting_restatement_state_hash check (state_hash_sha256 ~ '^[0-9a-f]{64}$')
);

create index if not exists ix_reporting_restatement_requests_series
    on __SCHEMA__.reporting_restatement_requests (tenant_id, series_id, created_at_utc desc);

create table if not exists __SCHEMA__.reporting_governance_audit (
    tenant_id text not null,
    aggregate_kind smallint not null,
    aggregate_id text not null,
    aggregate_version bigint not null,
    event_id text not null,
    occurred_at_utc timestamptz not null,
    previous_hash text null,
    event_hash text not null,
    event_payload text not null,
    payload_hash_sha256 text not null,
    primary key (tenant_id, aggregate_kind, aggregate_id, aggregate_version),
    unique (tenant_id, event_id),
    constraint ck_reporting_governance_audit_tenant
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_governance_audit_kind check (aggregate_kind between 0 and 1),
    constraint ck_reporting_governance_audit_version check (aggregate_version > 0),
    constraint ck_reporting_governance_audit_previous_hash
        check (previous_hash is null or previous_hash ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_governance_audit_event_hash check (event_hash ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_governance_audit_payload check (jsonb_typeof(event_payload::jsonb) = 'object'),
    constraint ck_reporting_governance_audit_payload_hash check (payload_hash_sha256 ~ '^[0-9a-f]{64}$')
);

create index if not exists ix_reporting_governance_audit_occurred
    on __SCHEMA__.reporting_governance_audit
        (tenant_id, aggregate_kind, aggregate_id, occurred_at_utc);

create or replace function __SCHEMA__.guard_reporting_governed_run_mutation()
returns trigger
language plpgsql
as $$
begin
    if tg_op = 'DELETE' then
        raise exception 'governed reporting revisions cannot be deleted'
            using errcode = '55000';
    end if;

    if old.governance_state = 4 then
        raise exception 'released reporting revisions are immutable'
            using errcode = '55000';
    end if;

    if new.tenant_id is distinct from old.tenant_id
       or new.run_id is distinct from old.run_id
       or new.series_id is distinct from old.series_id
       or new.revision is distinct from old.revision
       or new.organization_id is distinct from old.organization_id
       or new.company_id is distinct from old.company_id
       or new.created_at_utc is distinct from old.created_at_utc then
        raise exception 'governed reporting revision identity and scope are immutable'
            using errcode = '55000';
    end if;

    if new.aggregate_version <> old.aggregate_version + 1 then
        raise exception 'governed reporting revision updates must advance by one version'
            using errcode = '40001';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_reporting_governed_runs_guard
    on __SCHEMA__.reporting_governed_runs;

create trigger trg_reporting_governed_runs_guard
before update or delete on __SCHEMA__.reporting_governed_runs
for each row execute function __SCHEMA__.guard_reporting_governed_run_mutation();

create or replace function __SCHEMA__.guard_reporting_restatement_mutation()
returns trigger
language plpgsql
as $$
begin
    if tg_op = 'DELETE' then
        raise exception 'reporting restatement requests cannot be deleted'
            using errcode = '55000';
    end if;

    if new.tenant_id is distinct from old.tenant_id
       or new.request_id is distinct from old.request_id
       or new.series_id is distinct from old.series_id
       or new.predecessor_run_id is distinct from old.predecessor_run_id
       or new.created_at_utc is distinct from old.created_at_utc then
        raise exception 'reporting restatement request identity is immutable'
            using errcode = '55000';
    end if;

    if new.aggregate_version <> old.aggregate_version + 1 then
        raise exception 'reporting restatement request updates must advance by one version'
            using errcode = '40001';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_reporting_restatement_requests_guard
    on __SCHEMA__.reporting_restatement_requests;

create trigger trg_reporting_restatement_requests_guard
before update or delete on __SCHEMA__.reporting_restatement_requests
for each row execute function __SCHEMA__.guard_reporting_restatement_mutation();

create or replace function __SCHEMA__.validate_reporting_governance_audit_append()
returns trigger
language plpgsql
as $$
declare
    retained_previous_hash text;
begin
    if new.aggregate_version = 1 then
        if new.previous_hash is not null then
            raise exception 'the first reporting governance audit event cannot have a previous hash'
                using errcode = '55000';
        end if;

        if exists (
            select 1
            from __SCHEMA__.reporting_governance_audit
            where tenant_id = new.tenant_id
              and aggregate_kind = new.aggregate_kind
              and aggregate_id = new.aggregate_id) then
            raise exception 'the first reporting governance audit event already exists'
                using errcode = '40001';
        end if;
    else
        select event_hash
        into retained_previous_hash
        from __SCHEMA__.reporting_governance_audit
        where tenant_id = new.tenant_id
          and aggregate_kind = new.aggregate_kind
          and aggregate_id = new.aggregate_id
          and aggregate_version = new.aggregate_version - 1
        for key share;

        if not found then
            raise exception 'reporting governance audit versions must be contiguous'
                using errcode = '55000';
        end if;

        if new.previous_hash is distinct from retained_previous_hash then
            raise exception 'reporting governance audit previous hash does not match the retained chain'
                using errcode = '55000';
        end if;
    end if;

    return new;
end;
$$;

drop trigger if exists trg_reporting_governance_audit_append
    on __SCHEMA__.reporting_governance_audit;

create trigger trg_reporting_governance_audit_append
before insert on __SCHEMA__.reporting_governance_audit
for each row execute function __SCHEMA__.validate_reporting_governance_audit_append();

create or replace function __SCHEMA__.reject_reporting_governance_audit_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception 'reporting governance audit events are append-only'
        using errcode = '55000';
end;
$$;

drop trigger if exists trg_reporting_governance_audit_immutable
    on __SCHEMA__.reporting_governance_audit;

create trigger trg_reporting_governance_audit_immutable
before update or delete on __SCHEMA__.reporting_governance_audit
for each row execute function __SCHEMA__.reject_reporting_governance_audit_mutation();
