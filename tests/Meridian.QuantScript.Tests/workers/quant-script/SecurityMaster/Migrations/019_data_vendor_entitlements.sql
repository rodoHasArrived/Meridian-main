-- Migration 019: Data Vendor Entitlements
-- Stores licensing and contractual entitlement records for external data vendors
-- (e.g. CUSIP Global Services, SEDOL, Bloomberg BVAL, IDC, IHS Markit, Moody's, S&P).
-- Supports AUM-threshold tracking, renewal calendars, and expiry monitoring per the
-- Clearwater vendor licensing governance model.

create table if not exists __SCHEMA__.data_vendor_entitlements (
    entitlement_id                  uuid        not null primary key default gen_random_uuid(),
    vendor_name                     text        not null,
    data_type                       text        not null,
    contract_reference              text,
    effective_from                  timestamptz not null,
    effective_to                    timestamptz,
    aum_threshold_usd               numeric(28, 2),
    requires_direct_client_contract boolean     not null default false,
    contact_email                   text,
    renewal_reminder_days           int         not null default 30,
    is_active                       boolean     not null default true,
    created_by                      text        not null,
    created_at                      timestamptz not null default now()
);

create index if not exists ix_data_vendor_entitlements_vendor
    on __SCHEMA__.data_vendor_entitlements (vendor_name, data_type);

create index if not exists ix_data_vendor_entitlements_effective_to
    on __SCHEMA__.data_vendor_entitlements (effective_to)
    where effective_to is not null;
