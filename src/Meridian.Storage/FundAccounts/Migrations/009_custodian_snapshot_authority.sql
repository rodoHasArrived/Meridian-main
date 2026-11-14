-- Additive retained authority. Legacy batches remain explicitly unaccepted for planning.
alter table __SCHEMA__.custodian_statement_batch
    add column if not exists authority jsonb,
    add column if not exists content_sha256 text;

create index if not exists ix_custodian_snapshot_authority
    on __SCHEMA__.custodian_statement_batch(account_id, as_of_date desc, ingested_at desc);
