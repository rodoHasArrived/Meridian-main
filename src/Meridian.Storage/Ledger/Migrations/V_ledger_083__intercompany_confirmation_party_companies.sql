-- Migration: ledger / intercompany_confirmation_party_companies
-- Append-only: never rename or renumber applied migrations.
-- Reciprocal balance confirmations whose two books belong to different companies of one tenant (DEC-IC-CONFIRM-003).
-- Each side's owning company is retained so the confirmation is visible to both; rows written before this migration,
-- and same-company confirmations, leave both columns null and stay visible to company_id alone. Adding nullable
-- columns rewrites no retained version, so the append-only row triggers are unaffected.
alter table __SCHEMA__.intercompany_balance_confirmations add column if not exists party_one_company_id text null;
alter table __SCHEMA__.intercompany_balance_confirmations add column if not exists party_two_company_id text null;
create index if not exists ix_intercompany_balance_confirmation_party_one_company
    on __SCHEMA__.intercompany_balance_confirmations(tenant_id, party_one_company_id, accounting_arrangement_id, as_of)
    where party_one_company_id is not null;
create index if not exists ix_intercompany_balance_confirmation_party_two_company
    on __SCHEMA__.intercompany_balance_confirmations(tenant_id, party_two_company_id, accounting_arrangement_id, as_of)
    where party_two_company_id is not null;
-- One cross-company confirmation identity per tenant, profile and date, whichever side proposed it first.
create unique index if not exists ux_intercompany_balance_confirmation_cross_company_identity
    on __SCHEMA__.intercompany_balance_confirmations(tenant_id, accounting_arrangement_id, as_of)
    where version = 1 and party_one_company_id is not null;
