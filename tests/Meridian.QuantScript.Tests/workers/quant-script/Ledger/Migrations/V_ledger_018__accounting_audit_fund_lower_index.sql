-- Case-insensitive index for accounting_action_audit_events fund-profile lookups.
--
-- The audit-store list query matches fund_profile_id case-insensitively
-- (lower(fund_profile_id) = lower(@fund_profile_id)) so it stays consistent with the file store and the
-- publish-time run resolver, which both compare FundProfileId with OrdinalIgnoreCase. The raw-column
-- btree index ix_accounting_action_audit_events_scope cannot serve a lower() predicate, so without a
-- matching expression index the fund-profile tenant guard's per-field-edit lookup
-- (ListAsync(fund, ..., tenantId: null, companyId: null)) would degrade to a sequential scan on large
-- audit histories. This expression index restores index support for the case-insensitive predicate and
-- the recorded_at_utc ordering the query uses.
create index if not exists ix_accounting_action_audit_events_fund_lower
    on __SCHEMA__.accounting_action_audit_events(lower(fund_profile_id), recorded_at_utc desc);
