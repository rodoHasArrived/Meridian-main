-- Migration: ledger / recurring_journal_state
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- One retained recurring journal document (W10-JRNL-001): versioned schedule and template
-- definitions, activations and deterministic occurrence claims. The application holds a
-- session-level advisory lock for each intake cycle and verifies the document's content hash and
-- retained definitions on every read. Each commit advances `revision` by compare-and-swap so a
-- session that lost its lease cannot overwrite a newer commit. The row is created only by explicit
-- provisioning, and the retained row cannot be deleted, so lost state never reads as empty.
create table if not exists __SCHEMA__.recurring_journal_state (
    store_id text primary key check (store_id = 'default'),
    revision bigint not null check (revision > 0),
    content_hash text not null check (content_hash ~ '^[0-9a-f]{64}$'),
    envelope_json text not null check (jsonb_typeof(envelope_json::jsonb) = 'object'),
    initialized_at timestamptz not null,
    updated_at timestamptz not null
);

comment on table __SCHEMA__.recurring_journal_state is
    'Retained recurring journal definitions, activations and occurrence claims (W10-JRNL-001).';

create or replace function __SCHEMA__.reject_recurring_journal_state_delete()
returns trigger language plpgsql as $$
begin
    raise exception 'Retained recurring journal state cannot be deleted.'
        using errcode = '55000';
end;
$$;

do $migration$
begin
    if not exists (select 1 from pg_trigger
        where tgrelid = '__SCHEMA__.recurring_journal_state'::regclass
          and tgname = 'recurring_journal_state_retained') then
        create trigger recurring_journal_state_retained
            before delete on __SCHEMA__.recurring_journal_state
            for each row execute function __SCHEMA__.reject_recurring_journal_state_delete();
    end if;
end;
$migration$;
