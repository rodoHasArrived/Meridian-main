-- Migration 074: Security Master exception casework (review 2026-11-24, D2).
--
-- Owner decision 2026-11-25: a dedicated, deployment-global queue. Security Master conflicts,
-- identifier ambiguities and data-quality violations have no tenant or company owner, so they are not
-- projected into tenant-scoped reconciliation cases. One case per rule and subject (case_key); a
-- sync opens missing cases, refreshes last_seen_at, and closes cases whose source condition is gone.

create table if not exists __SCHEMA__.security_master_exception_cases (
    case_id        uuid primary key,
    case_key       text not null,
    category       text not null,
    rule_code      text not null,
    security_id    uuid null,
    severity       text not null,
    title          text not null,
    detail         text not null,
    status         text not null default 'Open',
    assignee       text null,
    opened_at      timestamptz not null,
    last_seen_at   timestamptz not null,
    sla_due_at     timestamptz not null,
    closed_at      timestamptz null,
    closed_reason  text null,
    constraint ck_security_master_exception_cases_status check (status in ('Open', 'Closed')),
    constraint ck_security_master_exception_cases_closed check ((status = 'Open') = (closed_at is null))
);

comment on table __SCHEMA__.security_master_exception_cases is
    'Review D2: deployment-global Security Master exception cases, one open case per rule and subject.';

create unique index if not exists ux_security_master_exception_cases_open_key
    on __SCHEMA__.security_master_exception_cases (case_key)
    where status = 'Open';

create index if not exists ix_security_master_exception_cases_open_due
    on __SCHEMA__.security_master_exception_cases (sla_due_at)
    where status = 'Open';
