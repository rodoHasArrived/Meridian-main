-- Ensure operational status persists for account-level mutation policy checks.
ALTER TABLE IF EXISTS __SCHEMA__.account_definition
    ADD COLUMN IF NOT EXISTS operational_status text NOT NULL DEFAULT 'Active';
