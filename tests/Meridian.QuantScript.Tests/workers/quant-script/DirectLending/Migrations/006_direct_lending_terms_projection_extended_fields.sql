alter table if exists __SCHEMA__.loan_terms_version
    add column if not exists interest_only_months integer not null default 0,
    add column if not exists grace_period_days integer,
    add column if not exists effective_rate_floor numeric(18,8),
    add column if not exists effective_rate_cap numeric(18,8),
    add column if not exists prepayment_penalty_rate numeric(18,8);
