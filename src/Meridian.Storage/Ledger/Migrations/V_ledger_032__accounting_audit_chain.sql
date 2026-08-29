-- Hash-chain the accounting-action audit family (W9-GOV-008 criterion 3, database posture).
--
-- accounting_action_audit_events originates in V_ledger_010; 017 added tenant scope and 018 an
-- index. Every row was appended with no predecessor hash, so the retained history is not
-- tamper-evident and nothing about it ever was. Adding chain columns and strict verification on top
-- of that history has exactly two failure modes and no third: verification rejects the retained
-- history outright, or it accepts it and thereby presents pre-upgrade events as tamper-evident when
-- nothing ever protected them.
--
-- This migration takes neither. It declares a boundary. genesis_sequence records where the chain
-- starts, pre_chain_event_count records how many retained rows fall outside it, and verification
-- reports those rows as unprotected rather than either rejecting them or vouching for them. Rows
-- before the genesis keep null chain columns; that is what "outside the chain" looks like on disk,
-- and it is why the columns are nullable rather than backfilled with invented hashes.
--
-- The head is a locked row rather than a value derived from the events, so concurrent writers cannot
-- fork the chain: an appender takes it FOR UPDATE, verifies it against the final retained event, and
-- advances it in the same transaction that inserts. That is the PostgresReportingArtifactAuditStore
-- scheme, followed deliberately rather than inventing a second one -- the file posture
-- (FileAccountingConfigurationStore) uses the same sequence/predecessor/payload digest, so an
-- operator does not have to learn two tamper-evidence models depending on which store is configured.
--
-- The file posture additionally needs its head retained OUTSIDE the artifact it protects, because a
-- whole-file snapshot store replaces the document on every write. Here the store cannot do that to
-- itself: the head row is a separate row under transactional control, and truncating the event table
-- leaves it pointing at a missing final event, which verification reports.

alter table __SCHEMA__.accounting_action_audit_events
    add column if not exists chain_sequence bigint null,
    add column if not exists payload_hash text null,
    add column if not exists previous_hash text null,
    add column if not exists entry_hash text null;

-- Digest shape is enforced at the column so a malformed hash cannot be written and then read back as
-- a mismatch, which would look like tampering rather than the write bug it is.
do $$
begin
    if not exists (
        select 1 from pg_constraint
        where conname = 'ck_accounting_audit_entry_hash_digest'
          and conrelid = '__SCHEMA__.accounting_action_audit_events'::regclass)
    then
        alter table __SCHEMA__.accounting_action_audit_events
            add constraint ck_accounting_audit_entry_hash_digest
            check (entry_hash is null or entry_hash ~ '^[0-9a-f]{64}$');
    end if;

    if not exists (
        select 1 from pg_constraint
        where conname = 'ck_accounting_audit_previous_hash_digest'
          and conrelid = '__SCHEMA__.accounting_action_audit_events'::regclass)
    then
        alter table __SCHEMA__.accounting_action_audit_events
            add constraint ck_accounting_audit_previous_hash_digest
            check (previous_hash is null or previous_hash ~ '^[0-9a-f]{64}$');
    end if;

    if not exists (
        select 1 from pg_constraint
        where conname = 'ck_accounting_audit_payload_hash_digest'
          and conrelid = '__SCHEMA__.accounting_action_audit_events'::regclass)
    then
        alter table __SCHEMA__.accounting_action_audit_events
            add constraint ck_accounting_audit_payload_hash_digest
            check (payload_hash is null or payload_hash ~ '^[0-9a-f]{64}$');
    end if;
end
$$;

-- Unique rather than merely indexed: two rows at one sequence is a forked chain, and the database is
-- the only place that can refuse it under concurrency.
create unique index if not exists ux_accounting_audit_chain_sequence
    on __SCHEMA__.accounting_action_audit_events (chain_sequence)
    where chain_sequence is not null;

create table if not exists __SCHEMA__.accounting_action_audit_chain_head (
    chain_id smallint primary key,
    schema_version integer not null default 1,
    next_sequence bigint not null,
    last_hash text null,
    -- Where the chain begins. Events retained before it are outside its guarantee, and verification
    -- says so rather than silently including or rejecting them.
    genesis_sequence bigint not null default 1,
    -- How many rows were already retained when chaining began: the declared size of the unprotected
    -- history, recorded once so a later reader cannot mistake "not chained" for "chain broken".
    pre_chain_event_count bigint not null default 0,
    genesis_recorded_at_utc timestamptz not null default now(),
    constraint ck_accounting_audit_chain_singleton
        check (chain_id = 1),
    constraint ck_accounting_audit_chain_next_sequence
        check (next_sequence > 0),
    constraint ck_accounting_audit_chain_genesis
        check (genesis_sequence > 0 and next_sequence >= genesis_sequence),
    constraint ck_accounting_audit_chain_last_hash
        check (last_hash is null or last_hash ~ '^[0-9a-f]{64}$')
);

-- Seed once, recording the pre-chain history as it stands at this moment. `do nothing` on conflict
-- so a re-run under the replay-every-script runner cannot reset a chain that has since advanced --
-- which would silently discard the head and let a truncated history verify.
insert into __SCHEMA__.accounting_action_audit_chain_head (
    chain_id,
    schema_version,
    next_sequence,
    last_hash,
    genesis_sequence,
    pre_chain_event_count)
select
    1,
    1,
    1,
    null,
    1,
    (select count(*) from __SCHEMA__.accounting_action_audit_events)
on conflict (chain_id) do nothing;
