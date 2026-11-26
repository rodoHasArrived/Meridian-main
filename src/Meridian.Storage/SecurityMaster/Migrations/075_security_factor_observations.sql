-- Migration 075: pool-factor observations (Security Master review 2026-11-24, C3).
--
-- Owner decision 2026-11-25: pool factors are observations, like fixings, not terms revisions. Each
-- row is an observed factor for an effective date with its source, evidence and recording time. The
-- table is append-only: a restated factor is a new row for the same effective date, and readers take
-- the latest recorded row per date. The terms version does not change when a factor is observed.

create table if not exists __SCHEMA__.security_factor_observations (
    observation_id  uuid primary key,
    security_id     uuid not null,
    effective_date  date not null,
    factor          numeric(20, 12) not null,
    source          text not null,
    evidence_ref    text null,
    recorded_at     timestamptz not null default now(),
    recorded_by     text not null,
    constraint ck_security_factor_observations_factor check (factor >= 0 and factor <= 1)
);

comment on table __SCHEMA__.security_factor_observations is
    'Review C3: append-only pool-factor observations; a restatement is a later row for the same effective date.';

create index if not exists ix_security_factor_observations_security_date
    on __SCHEMA__.security_factor_observations (security_id, effective_date, recorded_at);

create or replace function __SCHEMA__.reject_factor_observation_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'Security factor observations are append-only' using errcode = '23514';
end;
$$;

create trigger security_factor_observations_append_only
    before update or delete on __SCHEMA__.security_factor_observations
    for each row execute function __SCHEMA__.reject_factor_observation_mutation();
