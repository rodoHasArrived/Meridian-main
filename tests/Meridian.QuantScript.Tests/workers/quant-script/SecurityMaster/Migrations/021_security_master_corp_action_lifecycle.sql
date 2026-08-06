-- Migration 021: Corporate Action Lifecycle
-- Adds record-date, lifecycle-state, supersede-chain, and redemption-price columns to the
-- corporate-action event stream. A null lifecycle_state reads as Confirmed (events written
-- before lifecycle support); supersedes_corp_act_id links an amendment or cancellation to
-- the event it replaces so readers can fold chains without mutating history.

alter table __SCHEMA__.corporate_actions
    add column if not exists record_date date null;

alter table __SCHEMA__.corporate_actions
    add column if not exists lifecycle_state text null;

alter table __SCHEMA__.corporate_actions
    add column if not exists supersedes_corp_act_id uuid null;

alter table __SCHEMA__.corporate_actions
    add column if not exists redemption_price_percent_of_par numeric null;

create index if not exists ix_corporate_actions_supersedes
    on __SCHEMA__.corporate_actions (supersedes_corp_act_id)
    where supersedes_corp_act_id is not null;
