-- Migration: security-master / rate_repair_governance
-- The migration runner owns the transaction. Batches reference revisions through the review
-- context added by 069; frozen rosters are immutable and progress is versioned.
create table __SCHEMA__.security_rate_repair_batches (
    batch_id uuid primary key,
    tenant_id text not null,
    company_id text null,
    command_sha256 text not null check (command_sha256 ~ '^[a-f0-9]{64}$'),
    created_by text not null,
    created_at timestamptz not null,
    manifest_json jsonb not null
);
comment on table __SCHEMA__.security_rate_repair_batches is
    'Immutable rate-unit repair manifest retaining the exact selected roster, base versions, declared interpretation and evidence scope for an authenticated batch.';
create index ix_security_rate_repair_batches_tenant on __SCHEMA__.security_rate_repair_batches (tenant_id, created_at desc);
create table __SCHEMA__.security_rate_repair_state (
    batch_id uuid primary key references __SCHEMA__.security_rate_repair_batches(batch_id),
    version bigint not null check (version >= 1),
    state_json jsonb not null
);
comment on table __SCHEMA__.security_rate_repair_state is
    'Versioned compare-and-swap progress for a frozen repair batch, retaining each record draft, workflow, impact receipt and explicit item outcome while manifest authority remains immutable.';
create function __SCHEMA__.reject_rate_repair_manifest_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'Rate repair rosters and source evidence are immutable' using errcode = '23514';
end;
$$;
create trigger rate_repair_manifest_immutable before update or delete on __SCHEMA__.security_rate_repair_batches
    for each row execute function __SCHEMA__.reject_rate_repair_manifest_mutation();
create trigger rate_repair_manifest_no_truncate before truncate on __SCHEMA__.security_rate_repair_batches
    for each statement execute function __SCHEMA__.reject_rate_repair_manifest_mutation();
create trigger rate_repair_progress_no_delete before delete on __SCHEMA__.security_rate_repair_state
    for each row execute function __SCHEMA__.reject_rate_repair_manifest_mutation();
create trigger rate_repair_progress_no_truncate before truncate on __SCHEMA__.security_rate_repair_state
    for each statement execute function __SCHEMA__.reject_rate_repair_manifest_mutation();
