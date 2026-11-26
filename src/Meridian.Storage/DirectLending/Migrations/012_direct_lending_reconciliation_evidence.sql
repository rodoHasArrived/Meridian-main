-- Preserve exact request and source evidence for new reconciliations; legacy evidence stays unknown.
alter table __SCHEMA__.reconciliation_run add column if not exists evidence_json jsonb null;
alter table __SCHEMA__.reconciliation_result add column if not exists evidence_json jsonb null;
