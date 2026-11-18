-- Migration: ledger / journal_execution_lineage_indexes
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Index support for the execution-lineage journal lookup (order or fill -> journal entries).
-- PostgresLedgerJournalStore.BuildExecutionLineageFilter compares these exact metadata
-- expressions with equality, so each index matches one expression verbatim. The indexes are
-- partial: only postings that recorded the identifier are indexed, and an equality comparison
-- implies the expression is not null, so the planner can use them.

create index if not exists ix_journal_entries_metadata_fill_id
    on __SCHEMA__.journal_entries ((metadata ->> 'fillId'))
    where (metadata ->> 'fillId') is not null;

create index if not exists ix_journal_entries_metadata_source_order_id
    on __SCHEMA__.journal_entries ((metadata -> 'tags' ->> 'source.orderId'))
    where (metadata -> 'tags' ->> 'source.orderId') is not null;

create index if not exists ix_journal_entries_metadata_order_id
    on __SCHEMA__.journal_entries ((metadata ->> 'orderId'))
    where (metadata ->> 'orderId') is not null;
