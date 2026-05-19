create table if not exists __SCHEMA__.operations_workflow_audit (
    audit_id                      uuid primary key,
    occurred_at_utc               timestamptz not null,
    workflow_id                   text not null,
    fund_account_id               uuid not null,
    period_id                     text not null,
    event_type                    text not null,
    from_state                    text,
    to_state                      text,
    gate                          text,
    from_gate_status              text,
    to_gate_status                text,
    actor                         text not null,
    rationale                     text not null,
    trace_id                      text,
    request_id                    text,
    session_id                    text,
    run_id                        text,
    broker_reference_id           text,
    security_reference_id         text,
    ledger_reference_id           text,
    reconciliation_reference_id   text,
    evidence_reference_id         text,
    audit_reference_id            text,
    hash                          text not null,
    previous_hash                 text,
    severity                      text not null,
    tags                          text[] not null default '{}',
    created_at                    timestamptz not null default now(),
    constraint ck_operations_workflow_audit_event_type
        check (event_type in (
            'state_transition',
            'gate_change',
            'override_decision',
            'ledger_validate_decision',
            'ledger_post_decision',
            'break_opened',
            'break_assigned',
            'break_resolved',
            'break_closed',
            'approval_action',
            'close_action',
            'reopen_action'))
);

create index if not exists ix_operations_workflow_audit_stream
    on __SCHEMA__.operations_workflow_audit (workflow_id, occurred_at_utc, audit_id);

create unique index if not exists ux_operations_workflow_audit_hash
    on __SCHEMA__.operations_workflow_audit (workflow_id, hash);
