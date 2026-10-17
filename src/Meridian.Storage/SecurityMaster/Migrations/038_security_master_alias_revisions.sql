-- Append-only alias revision history. security_aliases remains the current-state row used by
-- identifier resolution and posting authority; every recorded change to an alias appends a revision
-- here first. Recorded-as-of reads select the latest revision recorded at or before the cutoff, so a
-- correction made today cannot change what an older as-of view reports. Pre-migration alias
-- revisions were not retained: the current-row backfill is available only from this migration,
-- and an earlier recorded-as-of lookup has unknown alias history, not evidence of no aliases.
create table if not exists __SCHEMA__.security_alias_revisions (
    alias_id uuid not null,
    revision integer not null,
    security_id uuid not null references __SCHEMA__.securities(security_id),
    alias_kind text not null,
    alias_value text not null,
    normalized_alias_value text not null,
    provider text null,
    normalized_provider text null,
    scope text not null,
    reason text null,
    valid_from timestamptz not null,
    valid_to timestamptz null,
    is_enabled boolean not null,
    is_retired boolean not null default false,
    recorded_by text not null,
    recorded_at timestamptz not null,
    history_available_from timestamptz null,
    primary key (alias_id, revision),
    constraint security_alias_revisions_revision_check check (revision >= 1)
);

create index if not exists ix_security_alias_revisions_security_recorded
    on __SCHEMA__.security_alias_revisions (security_id, alias_id, recorded_at);

comment on table __SCHEMA__.security_alias_revisions is
    'Append-only Security Master alias revisions. Recorded-as-of reads select the latest available revision at or before the cutoff. Pre-migration current-row baselines retain their creation metadata but are available only from history_available_from; earlier alias history is unknown.';
comment on column __SCHEMA__.security_alias_revisions.history_available_from is
    'Earliest recorded cutoff supported by a migrated current-row baseline; null for revisions recorded after history retention began. This boundary does not reconstruct discarded pre-migration revisions.';

-- Existing aliases become revision 1 with retained creation metadata. The current row may already
-- contain corrections whose prior revisions are lost, so it must not fabricate pre-migration
-- recorded knowledge. Normal append writes omit this marker and retain their actual recorded_at.
insert into __SCHEMA__.security_alias_revisions (
    alias_id, revision, security_id, alias_kind, alias_value, normalized_alias_value,
    provider, normalized_provider, scope, reason, valid_from, valid_to, is_enabled, is_retired,
    recorded_by, recorded_at, history_available_from)
select alias_id, 1, security_id, alias_kind, alias_value, normalized_alias_value,
       provider, normalized_provider, scope, reason, valid_from, valid_to, is_enabled, false,
       created_by, created_at, now()
from __SCHEMA__.security_aliases
on conflict (alias_id, revision) do nothing;

create function __SCHEMA__.reject_security_alias_revision_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'Security Master alias revisions are append-only' using errcode = '23514';
end;
$$;
create trigger security_alias_revisions_append_only
    before update or delete on __SCHEMA__.security_alias_revisions
    for each row execute function __SCHEMA__.reject_security_alias_revision_mutation();
create trigger security_alias_revisions_no_truncate
    before truncate on __SCHEMA__.security_alias_revisions
    for each statement execute function __SCHEMA__.reject_security_alias_revision_mutation();
