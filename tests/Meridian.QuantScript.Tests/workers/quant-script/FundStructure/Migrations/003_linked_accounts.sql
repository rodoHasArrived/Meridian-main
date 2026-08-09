-- Persists account node identities independently from active ownership links or assignments.
-- The in-memory fund-structure snapshot retains this set so disconnected account nodes keep
-- their kind and uniqueness semantics across restart and legacy migration.

CREATE TABLE IF NOT EXISTS __SCHEMA__.fund_structure_linked_account (
    account_id  uuid        NOT NULL PRIMARY KEY,
    updated_at  timestamptz NOT NULL DEFAULT now()
);
