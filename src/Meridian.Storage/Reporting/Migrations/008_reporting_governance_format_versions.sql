-- Existing rows were written by the committed v1 serializers and hash algorithm. They remain
-- byte-for-byte immutable and are explicitly classified as verified/read-only by the repository.
-- All post-migration writes must declare canonical v2.
alter table __SCHEMA__.reporting_governed_runs
    add column if not exists state_format_version smallint;

update __SCHEMA__.reporting_governed_runs
set state_format_version = 1
where state_format_version is null;

alter table __SCHEMA__.reporting_governed_runs
    alter column state_format_version drop default,
    alter column state_format_version set not null,
    drop constraint if exists ck_reporting_governed_state_format,
    add constraint ck_reporting_governed_state_format
        check (state_format_version in (1, 2)),
    drop constraint if exists ck_reporting_governed_company_optional_valid,
    drop constraint if exists ck_reporting_governed_company_required,
    add constraint ck_reporting_governed_company_required
        check (state_format_version = 1
            or (company_id is not null
                and company_id = btrim(company_id)
                and length(company_id) between 1 and 256)),
    drop constraint if exists ck_reporting_governed_fund_required,
    add constraint ck_reporting_governed_fund_required
        check (state_format_version = 1
            or coalesce(
                nullif(btrim(state_payload::jsonb #>> '{Scope,FundId}'), ''),
                nullif(btrim(state_payload::jsonb #>> '{scope,fundId}'), '')
            ) is not null);

alter table __SCHEMA__.reporting_restatement_requests
    add column if not exists state_format_version smallint;

update __SCHEMA__.reporting_restatement_requests
set state_format_version = 1
where state_format_version is null;

alter table __SCHEMA__.reporting_restatement_requests
    alter column state_format_version drop default,
    alter column state_format_version set not null,
    drop constraint if exists ck_reporting_restatement_state_format,
    add constraint ck_reporting_restatement_state_format
        check (state_format_version in (1, 2));

alter table __SCHEMA__.reporting_governance_audit
    add column if not exists hash_format_version smallint;

update __SCHEMA__.reporting_governance_audit
set hash_format_version = 1
where hash_format_version is null;

alter table __SCHEMA__.reporting_governance_audit
    alter column hash_format_version drop default,
    alter column hash_format_version set not null,
    drop constraint if exists ck_reporting_governance_audit_hash_format,
    add constraint ck_reporting_governance_audit_hash_format
        check (hash_format_version in (1, 2));

create or replace function __SCHEMA__.guard_reporting_governed_run_mutation()
returns trigger
language plpgsql
as $$
begin
    if tg_op = 'INSERT' then
        if new.state_format_version <> 2 then
            raise exception 'new governed reporting revisions must use canonical format v2'
                using errcode = '55000';
        end if;
        return new;
    end if;

    if tg_op = 'DELETE' then
        raise exception 'governed reporting revisions cannot be deleted'
            using errcode = '55000';
    end if;

    if old.state_format_version = 1 then
        raise exception 'legacy v1 governed reporting revisions are verified read-only and require fresh recertification'
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
       or new.state_format_version is distinct from old.state_format_version
       or new.created_at_utc is distinct from old.created_at_utc then
        raise exception 'governed reporting revision identity, scope, and format are immutable'
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
before insert or update or delete on __SCHEMA__.reporting_governed_runs
for each row execute function __SCHEMA__.guard_reporting_governed_run_mutation();

create or replace function __SCHEMA__.guard_reporting_restatement_mutation()
returns trigger
language plpgsql
as $$
begin
    if tg_op = 'INSERT' then
        if new.state_format_version <> 2 then
            raise exception 'new reporting restatement requests must use canonical format v2'
                using errcode = '55000';
        end if;
        return new;
    end if;

    if tg_op = 'DELETE' then
        raise exception 'reporting restatement requests cannot be deleted'
            using errcode = '55000';
    end if;

    if old.state_format_version = 1 then
        raise exception 'legacy v1 reporting restatement requests are verified read-only and require fresh recertification'
            using errcode = '55000';
    end if;

    if new.tenant_id is distinct from old.tenant_id
       or new.request_id is distinct from old.request_id
       or new.series_id is distinct from old.series_id
       or new.predecessor_run_id is distinct from old.predecessor_run_id
       or new.state_format_version is distinct from old.state_format_version
       or new.created_at_utc is distinct from old.created_at_utc then
        raise exception 'reporting restatement request identity and format are immutable'
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
before insert or update or delete on __SCHEMA__.reporting_restatement_requests
for each row execute function __SCHEMA__.guard_reporting_restatement_mutation();

create or replace function __SCHEMA__.validate_reporting_governance_audit_append()
returns trigger
language plpgsql
as $$
declare
    retained_previous_hash text;
begin
    if new.hash_format_version <> 2 then
        raise exception 'new reporting governance audit events must use canonical hash format v2'
            using errcode = '55000';
    end if;

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
