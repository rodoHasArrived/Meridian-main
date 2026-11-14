-- Immutable status history. Existing snapshots establish only what was known at this cutoff.
create table if not exists __SCHEMA__.entity_lifecycle_legacy_baseline (
    entity_id uuid primary key references __SCHEMA__.legal_entity(entity_id),
    lifecycle_status text not null,
    captured_at_utc timestamptz not null default now()
);
insert into __SCHEMA__.entity_lifecycle_legacy_baseline (entity_id, lifecycle_status)
select entity_id, lifecycle_status from __SCHEMA__.legal_entity
on conflict (entity_id) do nothing;

create table if not exists __SCHEMA__.entity_lifecycle_revision (
    tenant_id text not null check (tenant_id = lower(trim(tenant_id)) and length(tenant_id) > 0 and tenant_id <> 'all'),
    entity_id uuid not null references __SCHEMA__.legal_entity(entity_id),
    revision bigint not null check (revision > 0),
    recorded_at_utc timestamptz not null,
    recorded_by text not null check (length(trim(recorded_by)) > 0),
    content_hash text not null check (content_hash ~ '^[a-f0-9]{64}$'),
    previous_hash text not null,
    payload jsonb not null,
    primary key (tenant_id, entity_id, revision)
);

create or replace function __SCHEMA__.reject_entity_lifecycle_rewrite()
returns trigger language plpgsql as $$
begin
    raise exception 'Entity lifecycle history is immutable';
end;
$$;
create or replace trigger entity_lifecycle_revision_immutable
before update or delete on __SCHEMA__.entity_lifecycle_revision
for each row execute function __SCHEMA__.reject_entity_lifecycle_rewrite();
create or replace trigger entity_lifecycle_revision_no_truncate
before truncate on __SCHEMA__.entity_lifecycle_revision
for each statement execute function __SCHEMA__.reject_entity_lifecycle_rewrite();
create or replace trigger entity_lifecycle_baseline_immutable
before update or delete on __SCHEMA__.entity_lifecycle_legacy_baseline
for each row execute function __SCHEMA__.reject_entity_lifecycle_rewrite();
create or replace trigger entity_lifecycle_baseline_no_truncate
before truncate on __SCHEMA__.entity_lifecycle_legacy_baseline
for each statement execute function __SCHEMA__.reject_entity_lifecycle_rewrite();
