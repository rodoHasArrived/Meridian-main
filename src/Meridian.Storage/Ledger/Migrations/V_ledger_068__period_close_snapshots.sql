-- A hard close and its reconstructable journal population commit together. Reopening creates
-- another close version; historical snapshots are never overwritten or cascaded away.
create table if not exists __SCHEMA__.period_close_snapshots (
    period_id uuid not null references __SCHEMA__.accounting_periods(period_id),
    period_version bigint not null check (period_version > 0),
    ledger_book_id uuid null references __SCHEMA__.ledger_books(ledger_book_id),
    close_event_id uuid not null unique references __SCHEMA__.period_close_events(event_id),
    closed_at timestamptz not null,
    payload_json text not null check (jsonb_typeof(payload_json::jsonb) = 'object'),
    content_hash_sha256 text not null check (content_hash_sha256 ~ '^[a-f0-9]{64}$'),
    primary key (period_id, period_version)
);

create or replace function __SCHEMA__.reject_close_snapshot_mutation()
returns trigger language plpgsql as $$
begin
    raise exception 'Ledger close snapshots are immutable; retain a new close version.'
        using errcode = '55000';
end;
$$;

do $migration$
begin
    if not exists (select 1 from pg_trigger
        where tgrelid = '__SCHEMA__.period_close_snapshots'::regclass
          and tgname = 'close_snapshot_immutable') then
        create trigger close_snapshot_immutable
            before update or delete on __SCHEMA__.period_close_snapshots
            for each row execute function __SCHEMA__.reject_close_snapshot_mutation();
    end if;
end;
$migration$;
