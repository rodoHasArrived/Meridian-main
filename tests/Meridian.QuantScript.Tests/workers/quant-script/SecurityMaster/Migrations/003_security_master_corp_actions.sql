-- Migration 003: Corporate Action Events
-- Adds a dedicated event stream for corporate actions (dividends, splits, spin-offs, etc.)
-- keyed by security_id, ordered by ex_date.

create table if not exists __SCHEMA__.corporate_actions (
    corp_act_id uuid primary key,
    security_id uuid not null references __SCHEMA__.securities(security_id),
    event_type text not null,
    ex_date date not null,
    pay_date date null,
    dividend_per_share numeric null,
    currency text null,
    split_ratio numeric null,
    new_security_id uuid null,
    distribution_ratio numeric null,
    acquirer_security_id uuid null,
    exchange_ratio numeric null,
    subscription_price_per_share numeric null,
    rights_per_share numeric null,
    recorded_at timestamptz not null default now()
);

create index if not exists ix_corporate_actions_security_ex_date
    on __SCHEMA__.corporate_actions (security_id, ex_date);
