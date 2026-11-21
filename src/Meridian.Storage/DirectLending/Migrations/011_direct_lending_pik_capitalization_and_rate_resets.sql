-- Migration: direct-lending / direct_lending_pik_capitalization_and_rate_resets
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- PIK capitalization policy and floating-rate reset scheduling. Additive only: new tables and
-- nullable/defaulted columns, so the migration is re-runnable under the reapply drift policy and
-- older store versions can still start against it. Older builds do not carry the new terms or
-- servicing fields, so a loan that uses the new policies must not be saved by a mixed-version
-- writer fleet: upgrade every writer before enabling the policies on any loan.
-- Design: docs/engineering/blueprints/direct-lending-pik-capitalization-and-rate-resets.md.

-- Observed benchmark fixings. Insert-only: a correction is a new revision of the same
-- (index, observation date); the highest revision is the effective fixing. The declared value and
-- unit are retained next to the normalized fraction so the source number is never reinterpreted.
create table if not exists __SCHEMA__.loan_index_fixing (
    fixing_id uuid primary key,
    index_name text not null,
    observation_date date not null,
    revision integer not null,
    declared_value numeric(24,10) not null,
    declared_unit text not null,
    rate_fraction numeric(18,10) not null,
    source_ref text null,
    recorded_at timestamptz not null,
    recorded_by text null,
    constraint ck_loan_index_fixing_unit check (declared_unit in ('Fraction', 'Percent', 'BasisPoints')),
    constraint ck_loan_index_fixing_revision check (revision >= 1),
    constraint ck_loan_index_fixing_index_name check (index_name = upper(btrim(index_name)) and index_name <> ''),
    constraint ux_loan_index_fixing_revision unique (index_name, observation_date, revision)
);

-- Applied rate resets (manual and scheduled) for loans with a reset schedule; rebuilt from the
-- servicing state with every save, like accrual_entry_projection.
create table if not exists __SCHEMA__.loan_rate_reset_projection (
    loan_id uuid not null,
    effective_date date not null,
    reset_kind text not null,
    index_name text not null,
    fixing_observation_date date null,
    observed_rate numeric(18,10) not null,
    spread_bps numeric(18,6) not null,
    all_in_rate numeric(18,10) not null,
    source_ref text null,
    fixing_id uuid null,
    fixing_revision integer null,
    constraint pk_loan_rate_reset_projection primary key (loan_id, effective_date),
    constraint ck_loan_rate_reset_projection_kind check (reset_kind in ('Manual', 'Scheduled'))
);

-- PIK capitalizations: accrued PIK interest moved into principal on a policy date.
create table if not exists __SCHEMA__.loan_pik_capitalization_projection (
    loan_id uuid not null,
    capitalization_date date not null,
    period_start date not null,
    period_end date not null,
    amount numeric(24,8) not null,
    recorded_at timestamptz not null,
    constraint pk_loan_pik_capitalization_projection primary key (loan_id, capitalization_date),
    constraint ck_loan_pik_capitalization_projection_period check (period_start <= period_end)
);

-- PIK accrued but not yet capitalized, and the last capitalization date.
alter table __SCHEMA__.loan_servicing_projection
    add column if not exists pik_interest_accrued_unpaid numeric(24,8) not null default 0;
alter table __SCHEMA__.loan_servicing_projection
    add column if not exists last_pik_capitalization_date date null;

-- Whether an accrual's PIK amount waits for capitalization (policy) or capitalized the same day (legacy toggle).
alter table __SCHEMA__.accrual_entry_projection
    add column if not exists pik_capitalization_deferred boolean not null default false;

-- The opt-in policies on a terms version, retained as declared (units included).
alter table __SCHEMA__.loan_terms_version
    add column if not exists pik_capitalization_json jsonb null;
alter table __SCHEMA__.loan_terms_version
    add column if not exists rate_reset_schedule_json jsonb null;

comment on table __SCHEMA__.loan_index_fixing is
    'Observed benchmark fixings for scheduled direct-lending rate resets; insert-only, highest revision per (index_name, observation_date) is effective.';
comment on table __SCHEMA__.loan_rate_reset_projection is
    'Applied manual and scheduled rate resets for direct loans with a reset schedule; rebuilt from loan_state servicing on every save.';
comment on table __SCHEMA__.loan_pik_capitalization_projection is
    'PIK capitalizations moving accrued PIK interest into direct-loan principal; rebuilt from loan_state servicing on every save.';
