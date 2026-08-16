-- Migration 026: Operator Override Approval Workflow
-- Adds reviewer approval state and an append-only audit trail to operator overrides so the
-- Pending -> Approved/Rejected decision recorded by RecordApprovalDecisionAsync is durable and is
-- surfaced by GetAsync. Prior to this migration the approval fields on OperatorOverridesDto were
-- transient (never persisted), leaving the approve/reject workflow unreachable from any client.

alter table __SCHEMA__.security_operator_overrides
    add column if not exists approval_status text not null default 'NotRequested',
    add column if not exists reason_code text,
    add column if not exists reviewed_by text,
    add column if not exists reviewed_at timestamptz,
    add column if not exists audit_trail jsonb not null default '[]'::jsonb;
