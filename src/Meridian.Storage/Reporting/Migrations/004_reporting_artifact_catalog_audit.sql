create table if not exists __SCHEMA__.reporting_artifact_packages (
    tenant_id text not null,
    package_id text not null,
    package_hash_sha256 text not null,
    package_payload text not null,
    artifact_count integer not null,
    stored_at_utc timestamptz not null default now(),
    primary key (tenant_id, package_id),
    constraint ck_reporting_artifact_package_tenant
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_artifact_package_id
        check (package_id = btrim(package_id) and length(package_id) between 1 and 256),
    constraint ck_reporting_artifact_package_hash
        check (package_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_artifact_package_payload
        check (jsonb_typeof(package_payload::jsonb) = 'object'),
    constraint ck_reporting_artifact_package_count
        check (artifact_count between 1 and 10000)
);

create table if not exists __SCHEMA__.reporting_artifact_catalog (
    tenant_id text not null,
    package_id text not null,
    artifact_id text not null,
    artifact_hash_sha256 text not null,
    artifact_payload text not null,
    stored_at_utc timestamptz not null default now(),
    primary key (tenant_id, package_id, artifact_id),
    constraint fk_reporting_artifact_catalog_package
        foreign key (tenant_id, package_id)
        references __SCHEMA__.reporting_artifact_packages (tenant_id, package_id)
        on update restrict
        on delete restrict,
    constraint ck_reporting_artifact_catalog_tenant
        check (tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256),
    constraint ck_reporting_artifact_catalog_package_id
        check (package_id = btrim(package_id) and length(package_id) between 1 and 256),
    constraint ck_reporting_artifact_catalog_artifact_id
        check (artifact_id = btrim(artifact_id) and length(artifact_id) between 1 and 256),
    constraint ck_reporting_artifact_catalog_hash
        check (artifact_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_artifact_catalog_payload
        check (jsonb_typeof(artifact_payload::jsonb) = 'object')
);

create index if not exists ix_reporting_artifact_catalog_package
    on __SCHEMA__.reporting_artifact_catalog (tenant_id, package_id, artifact_id);

create table if not exists __SCHEMA__.reporting_artifact_audit_chain_head (
    chain_id smallint primary key,
    next_sequence bigint not null,
    last_hash text null,
    constraint ck_reporting_artifact_audit_chain_singleton
        check (chain_id = 1),
    constraint ck_reporting_artifact_audit_next_sequence
        check (next_sequence > 0),
    constraint ck_reporting_artifact_audit_last_hash
        check (last_hash is null or last_hash ~ '^[0-9a-f]{64}$')
);

insert into __SCHEMA__.reporting_artifact_audit_chain_head (
    chain_id,
    next_sequence,
    last_hash)
values (1, 1, null)
on conflict (chain_id) do nothing;

create table if not exists __SCHEMA__.reporting_artifact_audit (
    sequence bigint not null primary key,
    event_id text not null unique,
    occurred_at_utc timestamptz not null,
    action text not null,
    actor_tenant_id text not null,
    target_tenant_id text not null,
    package_id text not null,
    artifact_id text not null,
    previous_hash text null,
    entry_hash text not null,
    event_payload text not null,
    appended_at_utc timestamptz not null default now(),
    constraint ck_reporting_artifact_audit_sequence
        check (sequence > 0),
    constraint ck_reporting_artifact_audit_event_id
        check (event_id = btrim(event_id) and length(event_id) between 1 and 256),
    constraint ck_reporting_artifact_audit_action
        check (action in (
            'ArtifactRetained',
            'RetentionVerified',
            'ContentAccessed',
            'AccessDenied',
            'IntegrityFailure')),
    constraint ck_reporting_artifact_audit_actor_tenant
        check (actor_tenant_id = btrim(actor_tenant_id) and length(actor_tenant_id) between 1 and 256),
    constraint ck_reporting_artifact_audit_target_tenant
        check (target_tenant_id = btrim(target_tenant_id) and length(target_tenant_id) between 1 and 256),
    constraint ck_reporting_artifact_audit_package_id
        check (package_id = btrim(package_id) and length(package_id) between 1 and 256),
    constraint ck_reporting_artifact_audit_artifact_id
        check (artifact_id = btrim(artifact_id) and length(artifact_id) between 1 and 256),
    constraint ck_reporting_artifact_audit_previous_hash
        check (
            (sequence = 1 and previous_hash is null)
            or (sequence > 1 and previous_hash ~ '^[0-9a-f]{64}$')),
    constraint ck_reporting_artifact_audit_entry_hash
        check (entry_hash ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_artifact_audit_payload
        check (jsonb_typeof(event_payload::jsonb) = 'object')
);

create index if not exists ix_reporting_artifact_audit_tenant_package
    on __SCHEMA__.reporting_artifact_audit (
        target_tenant_id,
        package_id,
        sequence desc);

create or replace function __SCHEMA__.enforce_reporting_artifact_audit_append()
returns trigger
language plpgsql
as $$
declare
    expected_sequence bigint;
    expected_previous_hash text;
begin
    select next_sequence, last_hash
    into expected_sequence, expected_previous_hash
    from __SCHEMA__.reporting_artifact_audit_chain_head
    where chain_id = 1
    for update;

    if expected_sequence is null then
        raise exception 'reporting artifact audit chain head is missing'
            using errcode = '55000';
    end if;

    if new.sequence <> expected_sequence
       or new.previous_hash is distinct from expected_previous_hash then
        raise exception 'reporting artifact audit append does not extend the current chain head'
            using errcode = '55000';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_reporting_artifact_audit_append_guard
    on __SCHEMA__.reporting_artifact_audit;

create trigger trg_reporting_artifact_audit_append_guard
before insert on __SCHEMA__.reporting_artifact_audit
for each row execute function __SCHEMA__.enforce_reporting_artifact_audit_append();

create or replace function __SCHEMA__.reject_reporting_artifact_metadata_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception 'reporting artifact metadata and audit rows are immutable'
        using errcode = '55000';
end;
$$;

drop trigger if exists trg_reporting_artifact_packages_immutable
    on __SCHEMA__.reporting_artifact_packages;

create trigger trg_reporting_artifact_packages_immutable
before update or delete on __SCHEMA__.reporting_artifact_packages
for each row execute function __SCHEMA__.reject_reporting_artifact_metadata_mutation();

drop trigger if exists trg_reporting_artifact_catalog_immutable
    on __SCHEMA__.reporting_artifact_catalog;

create trigger trg_reporting_artifact_catalog_immutable
before update or delete on __SCHEMA__.reporting_artifact_catalog
for each row execute function __SCHEMA__.reject_reporting_artifact_metadata_mutation();

drop trigger if exists trg_reporting_artifact_audit_immutable
    on __SCHEMA__.reporting_artifact_audit;

create trigger trg_reporting_artifact_audit_immutable
before update or delete on __SCHEMA__.reporting_artifact_audit
for each row execute function __SCHEMA__.reject_reporting_artifact_metadata_mutation();
