-- Index support for durable ledger hydration and point-in-time journal reads.
-- The query path filters journal entries through accounting periods for ledger-book scope,
-- applies an occurred_at upper bound for as-of reads, then rehydrates full balanced entries.

create index if not exists ix_accounting_periods_ledger_book_period
    on __SCHEMA__.accounting_periods (ledger_book_id, period_id)
    where ledger_book_id is not null;

create index if not exists ix_journal_entries_period_as_of
    on __SCHEMA__.journal_entries (period_id, occurred_at, global_sequence, journal_entry_id);

create index if not exists ix_journal_entries_as_of
    on __SCHEMA__.journal_entries (occurred_at, global_sequence, journal_entry_id);
