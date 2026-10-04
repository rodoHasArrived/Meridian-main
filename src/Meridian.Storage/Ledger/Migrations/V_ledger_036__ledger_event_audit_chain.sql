-- Durable ledger-event audit, separate from accounting configuration audit (V_ledger_032).
-- Existing facts are an explicit unprotected genesis boundary, never backfilled with hashes.
create table if not exists __SCHEMA__.ledger_event_audit_head (
    chain_id smallint primary key check (chain_id = 1),
    schema_version integer not null default 1,
    next_sequence bigint not null default 1 check (next_sequence > 0),
    last_hash text null check (last_hash is null or last_hash ~ '^[0-9a-f]{64}$'),
    genesis_hash text not null default '' check (genesis_hash = '' or genesis_hash ~ '^[0-9a-f]{64}$'),
    genesis_at_utc timestamptz not null default now()
);

create table if not exists __SCHEMA__.ledger_event_audit_genesis (
    subject_kind text not null check (subject_kind in ('journal', 'period', 'period-close')),
    subject_id uuid not null,
    subject_version bigint not null,
    primary key (subject_kind, subject_id)
);

create table if not exists __SCHEMA__.ledger_event_audit_events (
    chain_sequence bigint primary key check (chain_sequence > 0),
    subject_kind text not null check (subject_kind in ('journal', 'period')),
    subject_id uuid not null,
    subject_version bigint not null check (subject_version > 0),
    action text not null,
    actor text null,
    recorded_at_utc timestamptz not null,
    fact_snapshot text not null,
    close_event_id uuid null,
    close_event_snapshot text null,
    payload_hash text not null check (payload_hash ~ '^[0-9a-f]{64}$'),
    previous_hash text null check (previous_hash is null or previous_hash ~ '^[0-9a-f]{64}$'),
    entry_hash text not null check (entry_hash ~ '^[0-9a-f]{64}$'),
    unique (subject_kind, subject_id, subject_version),
    check ((close_event_id is null) = (close_event_snapshot is null))
);
create unique index if not exists ux_ledger_event_audit_close_event
    on __SCHEMA__.ledger_event_audit_events (close_event_id) where close_event_id is not null;

-- On explicit reruns, only the first head insertion captures genesis identities;
-- startup must never convert newly uncovered facts into exempt legacy history.
lock table __SCHEMA__.journal_entries, __SCHEMA__.accounting_periods,
    __SCHEMA__.period_close_events in share row exclusive mode;
do $$
begin
    if not exists (select 1 from __SCHEMA__.ledger_event_audit_head)
       and (exists (select 1 from __SCHEMA__.ledger_event_audit_events)
            or exists (select 1 from __SCHEMA__.ledger_event_audit_genesis)) then
        raise exception 'retained ledger audit head is missing; genesis cannot be recreated';
    end if;
end
$$;
do $genesis$
declare inserted integer;
begin
    insert into __SCHEMA__.ledger_event_audit_head (chain_id) values (1)
    on conflict (chain_id) do nothing;
    get diagnostics inserted = row_count;
    if inserted = 1 then
        insert into __SCHEMA__.ledger_event_audit_genesis (subject_kind, subject_id, subject_version)
        select 'journal', journal_entry_id, 1 from __SCHEMA__.journal_entries
        union all select 'period', period_id, optimistic_version from __SCHEMA__.accounting_periods
        union all select 'period-close', event_id, period_version from __SCHEMA__.period_close_events;
        -- Protect the boundary inventory, not the pre-upgrade economic contents.
        update __SCHEMA__.ledger_event_audit_head set genesis_hash = (
            select encode(sha256(convert_to(coalesce(string_agg(
                subject_kind || ':' || subject_id::text || ':' || subject_version::text || E'\n',
                '' order by subject_kind collate "C", subject_id), ''), 'UTF8')), 'hex')
            from __SCHEMA__.ledger_event_audit_genesis
        ) where chain_id = 1;
    end if;
end
$genesis$;

comment on table __SCHEMA__.ledger_event_audit_events is
    'Ledger facts and actor attribution, hash chained in the same transaction as journal and period mutations. Null actor explicitly means unattributed; it is never synthesized from an approver. Verification checks covered facts as well as links. Coherent rollback of the head, suffix and corresponding facts requires an external retained checkpoint to detect.';
