-- Preserve the operator that retained bank-side transaction evidence.
-- Schema placeholder replaced at runtime: __SCHEMA__

ALTER TABLE __SCHEMA__.bank_transactions
    ADD COLUMN IF NOT EXISTS recorded_by TEXT;
