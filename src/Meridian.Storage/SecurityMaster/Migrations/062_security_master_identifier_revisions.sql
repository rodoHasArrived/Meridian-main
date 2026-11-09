-- Migration 062: Append-only recorded history of Security Master identifier rows.
-- security_identifiers remains the reconstructible current-state projection used by identifier
-- lookup and posting authority; projection replacement still replaces its rows. That replacement
-- used to leave no record of what the rows said before, and it reset source, confidence and
-- manual_override on every write. Each replacement now appends a revision here, in the same
-- transaction, for every identifier row it adds, amends, expires or removes; an unchanged replay
-- appends nothing. Valid time (valid_from/valid_to) is a column of each revision; recorded time is
-- recorded_at, so valid-time identity and recorded history stay separate questions.
--
-- Economic identifier values were already retained by the append-only security_events payloads,
-- which the recorded identifier lookup replays. Those payloads do not carry the row-level
-- source/confidence/manual_override metadata, nor projection-only writes such as rebuilds; this
-- table records what the identifier projection actually held and when.
--
-- Pre-migration identifier rows become a 'Baseline' revision available only from this migration
-- (history_available_from). Their earlier history was never retained, so an earlier recorded
-- cutoff has unknown identifier history, not evidence of no identifiers. Nothing in
-- security_identifiers is changed by this migration.
create table if not exists __SCHEMA__.security_identifier_revisions (
    security_id uuid not null references __SCHEMA__.securities(security_id),
    identifier_kind text not null,
    identifier_value text not null,
    valid_from timestamptz not null,
    revision integer not null,
    change_kind text not null,
    normalized_identifier_value text not null,
    provider text null,
    normalized_provider text null,
    is_primary boolean not null,
    valid_to timestamptz null,
    source text not null,
    confidence numeric(5,4) null,
    manual_override boolean not null,
    recorded_by text not null,
    recorded_at timestamptz not null,
    history_available_from timestamptz null,
    primary key (security_id, identifier_kind, identifier_value, valid_from, revision),
    constraint security_identifier_revisions_revision_check check (revision >= 1),
    constraint security_identifier_revisions_change_kind_check
        check (change_kind in ('Baseline', 'Added', 'Amended', 'Expired', 'Removed'))
);

create index if not exists ix_security_identifier_revisions_security_recorded
    on __SCHEMA__.security_identifier_revisions (security_id, recorded_at);

comment on table __SCHEMA__.security_identifier_revisions is
    'Append-only recorded history of security_identifiers rows. Each projection replacement appends Added/Amended/Expired/Removed revisions in the same transaction; recorded-as-of reads select the latest revision per row identity at or before the cutoff. Pre-migration rows are Baseline revisions available only from history_available_from.';
comment on column __SCHEMA__.security_identifier_revisions.change_kind is
    'Baseline (pre-migration current row), Added, Amended, Expired (valid_to first set) or Removed (row no longer in the projection; values repeat the last retained revision).';
comment on column __SCHEMA__.security_identifier_revisions.history_available_from is
    'Earliest recorded cutoff supported by a migrated Baseline revision; null for revisions recorded after history retention began. This boundary does not reconstruct discarded pre-migration history.';

insert into __SCHEMA__.security_identifier_revisions (
    security_id, identifier_kind, identifier_value, valid_from, revision, change_kind,
    normalized_identifier_value, provider, normalized_provider, is_primary, valid_to,
    source, confidence, manual_override, recorded_by, recorded_at, history_available_from)
select security_id, identifier_kind, identifier_value, valid_from, 1, 'Baseline',
       normalized_identifier_value, provider, normalized_provider, is_primary, valid_to,
       source, confidence, manual_override, 'security-master.migration-062', now(), now()
from __SCHEMA__.security_identifiers
on conflict (security_id, identifier_kind, identifier_value, valid_from, revision) do nothing;

create or replace function __SCHEMA__.reject_security_identifier_revision_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'Security Master identifier revisions are append-only' using errcode = '23514';
end;
$$;

create trigger security_identifier_revisions_append_only
    before update or delete on __SCHEMA__.security_identifier_revisions
    for each row execute function __SCHEMA__.reject_security_identifier_revision_mutation();

create trigger security_identifier_revisions_no_truncate
    before truncate on __SCHEMA__.security_identifier_revisions
    for each statement execute function __SCHEMA__.reject_security_identifier_revision_mutation();
