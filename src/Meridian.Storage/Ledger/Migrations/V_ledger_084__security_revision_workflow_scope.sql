-- Migration: ledger / security_revision_workflow_scope
-- Retained workflows without a purpose remain Close. Revision approvals do not consume a close lane.
-- The migration runner owns the transaction.
drop index if exists __SCHEMA__.ux_operations_continuity_open_workflow;
create unique index ux_operations_continuity_open_workflow
    on __SCHEMA__.operations_continuity_workflows (fund_account_id, period_id)
    where derived_status <> 'Closed' and coalesce(workflow_json ->> 'purpose', 'Close') = 'Close';
create unique index ux_operations_continuity_revision_subject
    on __SCHEMA__.operations_continuity_workflows (((workflow_json ->> 'subjectRevisionId')::uuid))
    where workflow_json ->> 'purpose' = 'SecurityMasterRevision';
alter table __SCHEMA__.operations_continuity_workflows
    add constraint operations_workflow_purpose_check check (
        (not (workflow_json ? 'purpose') or jsonb_typeof(workflow_json -> 'purpose') = 'string')
        and coalesce(workflow_json ->> 'purpose', 'Close') in ('Close', 'SecurityMasterRevision')
        and ((coalesce(workflow_json ->> 'purpose', 'Close') = 'Close'
              and workflow_json ->> 'subjectRevisionId' is null
              and workflow_json ->> 'subjectMaker' is null
              and workflow_json ->> 'subjectEvidenceId' is null
              and workflow_json ->> 'subjectEvidenceSha256' is null)
             or (coalesce(workflow_json ->> 'purpose', 'Close') = 'SecurityMasterRevision'
                 and workflow_json ->> 'subjectRevisionId' is not null
                 and workflow_json ->> 'subjectMaker' is not null
                 and length(trim(workflow_json ->> 'subjectMaker')) > 0
                 and workflow_json ->> 'subjectEvidenceId' is not null
                 and length(trim(workflow_json ->> 'subjectEvidenceId')) > 0
                 and workflow_json ->> 'subjectEvidenceSha256' is not null
                 and workflow_json ->> 'subjectEvidenceSha256' ~ '^[a-f0-9]{64}$')));
create function __SCHEMA__.protect_operations_workflow_subject() returns trigger
language plpgsql as $$
begin
    if coalesce(new.workflow_json ->> 'purpose', 'Close') is distinct from coalesce(old.workflow_json ->> 'purpose', 'Close')
       or new.workflow_json ->> 'subjectRevisionId' is distinct from old.workflow_json ->> 'subjectRevisionId'
       or new.workflow_json ->> 'subjectMaker' is distinct from old.workflow_json ->> 'subjectMaker'
       or new.workflow_json ->> 'subjectEvidenceId' is distinct from old.workflow_json ->> 'subjectEvidenceId'
       or new.workflow_json ->> 'subjectEvidenceSha256' is distinct from old.workflow_json ->> 'subjectEvidenceSha256' then
        raise exception 'Workflow purpose, subject and approval evidence are immutable' using errcode = '23514';
    end if;
    if old.workflow_json ->> 'purpose' = 'SecurityMasterRevision'
       and (new.fund_account_id is distinct from old.fund_account_id
            or new.period_id is distinct from old.period_id
            or new.workflow_json ->> 'ledgerBookId' is distinct from old.workflow_json ->> 'ledgerBookId') then
        raise exception 'Revision workflow accounting scope is immutable' using errcode = '23514';
    end if;
    return new;
end;
$$;
create trigger operations_workflow_subject_immutable before update on __SCHEMA__.operations_continuity_workflows
    for each row execute function __SCHEMA__.protect_operations_workflow_subject();
