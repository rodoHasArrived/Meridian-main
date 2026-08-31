-- Scope requirements became stronger after the committed v1 governance format. Existing v1 rows
-- may legitimately lack company/fund facts, so this migration normalizes the column constraint
-- without rewriting or rejecting their immutable evidence. Migration 008 applies the strict
-- company/fund requirement conditionally to canonical v2 rows after formats are explicit.
alter table __SCHEMA__.reporting_governed_runs
    drop constraint if exists ck_reporting_governed_company_required,
    add constraint ck_reporting_governed_company_optional_valid
        check (company_id is null
            or (company_id = btrim(company_id) and length(company_id) between 1 and 256)),
    drop constraint if exists ck_reporting_governed_fund_required;
