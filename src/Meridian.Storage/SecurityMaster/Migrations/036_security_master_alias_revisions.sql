-- Append-only alias revision history. security_aliases remains the current-state row used by
-- identifier resolution and posting authority; every recorded change to an alias appends a revision
-- here first. Recorded-as-of reads select the latest revision recorded at or before the cutoff, so a
-- correction made today cannot change what an older as-of view reports.
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
    primary key (alias_id, revision),
    constraint security_alias_revisions_revision_check check (revision >= 1)
);

create index if not exists ix_security_alias_revisions_security_recorded
    on __SCHEMA__.security_alias_revisions (security_id, alias_id, recorded_at);

comment on table __SCHEMA__.security_alias_revisions is
    'Append-only Security Master alias revisions. Revision 1 carries the alias creation facts; later revisions record corrections or retirement with the recording actor and timestamp, and recorded-as-of alias reads select the latest revision recorded at or before the cutoff.';

-- Existing aliases become revision 1, recorded when the alias was created. This matches the
-- recorded-as-of filter that previously read created_at from the current row.
insert into __SCHEMA__.security_alias_revisions (
    alias_id, revision, security_id, alias_kind, alias_value, normalized_alias_value,
    provider, normalized_provider, scope, reason, valid_from, valid_to, is_enabled, is_retired,
    recorded_by, recorded_at)
select alias_id, 1, security_id, alias_kind, alias_value, normalized_alias_value,
       provider, normalized_provider, scope, reason, valid_from, valid_to, is_enabled, false,
       created_by, created_at
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
