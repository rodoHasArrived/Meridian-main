-- Migration: security-master / security_revision_review_context
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
