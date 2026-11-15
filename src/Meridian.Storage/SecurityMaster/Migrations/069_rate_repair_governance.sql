-- Migration: security-master / rate_repair_governance
-- The migration runner owns the transaction. Legacy maker/submitter identities remain unknown.
alter table __SCHEMA__.security_master_revisions
    add column if not exists drafted_by text null,
    add column if not exists submitted_by text null,
    add column if not exists repair_batch_id uuid null,
    add column if not exists repair_evidence_id text null,
    add column if not exists repair_evidence_sha256 text null,
    add column if not exists reviewed_impact_receipt_id text null,
    add column if not exists reviewed_canonical_version bigint null;

alter table __SCHEMA__.security_master_revisions
    add constraint security_revision_repair_context_check check (
        repair_batch_id is null or
        (drafted_by is not null and length(trim(drafted_by)) > 0
         and repair_evidence_id is not null and length(trim(repair_evidence_id)) > 0
         and repair_evidence_sha256 is not null and repair_evidence_sha256 ~ '^[a-f0-9]{64}$'
         and base_version is not null and base_version >= 1 and field_base_value is not null
         and field_base_recorded and field_value_recorded));
alter table __SCHEMA__.security_master_revisions
    add constraint security_revision_impact_binding_check check (
        (reviewed_impact_receipt_id is null) = (reviewed_canonical_version is null)
        and (reviewed_impact_receipt_id is null
             or (reviewed_impact_receipt_id ~ '^[a-f0-9]{64}$' and reviewed_canonical_version >= 1)));

create function __SCHEMA__.protect_security_revision_review_context() returns trigger
language plpgsql as $$
begin
    if new.drafted_by is distinct from old.drafted_by
       or new.repair_batch_id is distinct from old.repair_batch_id
       or new.repair_evidence_id is distinct from old.repair_evidence_id
       or new.repair_evidence_sha256 is distinct from old.repair_evidence_sha256
       or (old.submitted_by is not null and new.submitted_by is distinct from old.submitted_by)
       or (old.reviewed_impact_receipt_id is not null and
           (new.reviewed_impact_receipt_id is distinct from old.reviewed_impact_receipt_id
            or new.reviewed_canonical_version is distinct from old.reviewed_canonical_version)) then
        raise exception 'Security revision maker and retained review context are immutable' using errcode = '23514';
    end if;
    return new;
end;
$$;
create trigger security_revision_review_context_immutable
    before update on __SCHEMA__.security_master_revisions
    for each row execute function __SCHEMA__.protect_security_revision_review_context();

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
