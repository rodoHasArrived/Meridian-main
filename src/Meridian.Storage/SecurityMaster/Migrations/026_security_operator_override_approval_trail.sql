-- Migration 026: Operator Override Approval Trail
-- Promotes the operator-override approval lifecycle to durable columns so approval status, the reviewer
-- decision, and the append-only audit trail survive reads (Wave 1 / Track C). Existing rows default to
-- an unrequested overlay with an empty trail; a value change re-opens review (enforced in the store).

alter table __SCHEMA__.security_operator_overrides
    add column if not exists approval_status text        not null default 'NotRequested',
    add column if not exists reason_code     text,
    add column if not exists reviewed_by     text,
    add column if not exists reviewed_at     timestamptz,
    add column if not exists audit_trail     jsonb       not null default '[]'::jsonb;
