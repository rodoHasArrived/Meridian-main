-- Migration 023: Security Master Data Quality Reports
-- Persists the latest data quality run results including all rule violations
-- across the 8 Clearwater control categories (completeness, format, minimum-attribute,
-- taxonomy, consistency, refresh, staleness, external-comparison).

create table if not exists __SCHEMA__.security_master_quality_reports (
    id          bigserial   primary key,
    run_at      timestamptz not null,
    report_json jsonb       not null
);

create index if not exists ix_security_master_quality_reports_run_at
    on __SCHEMA__.security_master_quality_reports (run_at desc);
