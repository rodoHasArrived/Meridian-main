-- P1 bilateral agreements have a mutable version pointer and immutable retained snapshots.
-- Tenant identity is mandatory from the first insert; legacy unattributed entities are not
-- automatically adopted by the registry. No ledger database foreign key is assumed.
create table if not exists __SCHEMA__.intercompany_agreement (
    tenant_id text not null check (tenant_id = lower(btrim(tenant_id)) and length(tenant_id) > 0),
    agreement_id uuid not null,
    aggregate_version bigint not null check (aggregate_version > 0),
    primary key (tenant_id, agreement_id)
);

create table if not exists __SCHEMA__.intercompany_agreement_snapshot (
    tenant_id text not null,
    agreement_id uuid not null,
    aggregate_version bigint not null check (aggregate_version > 0),
    payload jsonb not null,
    retained_at_utc timestamptz not null default now(),
    primary key (tenant_id, agreement_id, aggregate_version),
    foreign key (tenant_id, agreement_id)
        references __SCHEMA__.intercompany_agreement (tenant_id, agreement_id)
);

create table if not exists __SCHEMA__.intercompany_agreement_revision (
    tenant_id text not null,
    agreement_id uuid not null,
    agreement_version integer not null check (agreement_version > 0),
    payload jsonb not null,
    primary key (tenant_id, agreement_id, agreement_version),
    foreign key (tenant_id, agreement_id)
        references __SCHEMA__.intercompany_agreement (tenant_id, agreement_id)
);

create table if not exists __SCHEMA__.intercompany_agreement_audit (
    tenant_id text not null,
    agreement_id uuid not null,
    sequence_number integer not null check (sequence_number > 0),
    payload jsonb not null,
    primary key (tenant_id, agreement_id, sequence_number),
    foreign key (tenant_id, agreement_id)
        references __SCHEMA__.intercompany_agreement (tenant_id, agreement_id)
);

create table if not exists __SCHEMA__.intercompany_agreement_command_receipt (
    tenant_id text not null,
    agreement_id uuid not null,
    command_id uuid not null,
    payload jsonb not null,
    primary key (tenant_id, agreement_id, command_id),
    foreign key (tenant_id, agreement_id)
        references __SCHEMA__.intercompany_agreement (tenant_id, agreement_id)
);

-- A command cannot be replayed with a different agreement ID in the same tenant.
create unique index if not exists ix_intercompany_agreement_command_tenant
    on __SCHEMA__.intercompany_agreement_command_receipt (tenant_id, command_id);

-- Keep the head honest even when a caller accidentally bypasses the application command path.
-- The deferred pointer constraint permits inserting a head and its first snapshot atomically.
do $$
begin
    if not exists (
        select 1 from pg_constraint
        where conrelid = '__SCHEMA__.intercompany_agreement'::regclass
          and conname = 'intercompany_agreement_snapshot_pointer') then
        alter table __SCHEMA__.intercompany_agreement
            add constraint intercompany_agreement_snapshot_pointer
            foreign key (tenant_id, agreement_id, aggregate_version)
            references __SCHEMA__.intercompany_agreement_snapshot (tenant_id, agreement_id, aggregate_version)
            deferrable initially deferred;
    end if;
end;
$$;

create or replace function __SCHEMA__.reject_intercompany_agreement_history_rewrite()
returns trigger language plpgsql as $$
begin
    raise exception 'Intercompany agreement history, revisions and command receipts are immutable';
end;
$$;

create or replace function __SCHEMA__.guard_intercompany_agreement_head()
returns trigger language plpgsql as $$
begin
    if tg_op = 'DELETE' or tg_op = 'TRUNCATE' then
        raise exception 'Intercompany agreement heads cannot be removed';
    end if;
    if new.tenant_id is distinct from old.tenant_id
       or new.agreement_id is distinct from old.agreement_id
       or new.aggregate_version <> old.aggregate_version + 1 then
        raise exception 'Intercompany agreement head must retain its identity and advance exactly one version';
    end if;
    return new;
end;
$$;

create or replace trigger intercompany_agreement_head_guard
before update or delete on __SCHEMA__.intercompany_agreement
for each row execute function __SCHEMA__.guard_intercompany_agreement_head();
create or replace trigger intercompany_agreement_head_no_truncate
before truncate on __SCHEMA__.intercompany_agreement
for each statement execute function __SCHEMA__.guard_intercompany_agreement_head();

create or replace trigger intercompany_agreement_snapshot_immutable
before update or delete on __SCHEMA__.intercompany_agreement_snapshot
for each row execute function __SCHEMA__.reject_intercompany_agreement_history_rewrite();
create or replace trigger intercompany_agreement_snapshot_no_truncate
before truncate on __SCHEMA__.intercompany_agreement_snapshot
for each statement execute function __SCHEMA__.reject_intercompany_agreement_history_rewrite();
create or replace trigger intercompany_agreement_revision_immutable
before update or delete on __SCHEMA__.intercompany_agreement_revision
for each row execute function __SCHEMA__.reject_intercompany_agreement_history_rewrite();
create or replace trigger intercompany_agreement_revision_no_truncate
before truncate on __SCHEMA__.intercompany_agreement_revision
for each statement execute function __SCHEMA__.reject_intercompany_agreement_history_rewrite();
create or replace trigger intercompany_agreement_audit_immutable
before update or delete on __SCHEMA__.intercompany_agreement_audit
for each row execute function __SCHEMA__.reject_intercompany_agreement_history_rewrite();
create or replace trigger intercompany_agreement_audit_no_truncate
before truncate on __SCHEMA__.intercompany_agreement_audit
for each statement execute function __SCHEMA__.reject_intercompany_agreement_history_rewrite();
create or replace trigger intercompany_agreement_receipt_immutable
before update or delete on __SCHEMA__.intercompany_agreement_command_receipt
for each row execute function __SCHEMA__.reject_intercompany_agreement_history_rewrite();
create or replace trigger intercompany_agreement_receipt_no_truncate
before truncate on __SCHEMA__.intercompany_agreement_command_receipt
for each statement execute function __SCHEMA__.reject_intercompany_agreement_history_rewrite();

comment on table __SCHEMA__.intercompany_agreement_snapshot is
    'Immutable command snapshots; the agreement head points to one exact aggregate version.';
comment on table __SCHEMA__.intercompany_agreement_revision is
    'Approved agreement terms and termination decisions; no historical payload is rewritten.';
