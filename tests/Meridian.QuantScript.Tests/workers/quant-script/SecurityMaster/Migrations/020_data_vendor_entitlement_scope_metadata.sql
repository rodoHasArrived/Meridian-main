-- Migration 020: Data Vendor Entitlement Scope Metadata
-- Adds client/account/fund/security applicability and operator-configured source metadata
-- to vendor entitlement records while preserving existing global entitlement behavior.

alter table __SCHEMA__.data_vendor_entitlements
    add column if not exists client_id text,
    add column if not exists account_id text,
    add column if not exists fund_profile_id text,
    add column if not exists security_id uuid,
    add column if not exists source_category text,
    add column if not exists expected_refresh_cadence text,
    add column if not exists default_max_days_stale int,
    add column if not exists operator_metadata text;

create index if not exists ix_data_vendor_entitlements_scope
    on __SCHEMA__.data_vendor_entitlements (vendor_name, data_type, client_id, fund_profile_id, account_id, security_id);
