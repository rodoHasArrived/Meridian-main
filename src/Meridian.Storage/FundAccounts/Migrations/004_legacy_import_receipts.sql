-- Durable receipt for the one-time legacy JSON snapshot import.
-- The receipt is written in the same transaction as imported account data.

CREATE TABLE IF NOT EXISTS __SCHEMA__.fund_account_legacy_import_receipt (
    source_hash     text        NOT NULL PRIMARY KEY,
    imported_at    timestamptz NOT NULL DEFAULT now(),
    account_count  integer     NOT NULL,
    CONSTRAINT ck_fund_account_legacy_import_receipt_source_hash
        CHECK (source_hash ~ '^[0-9a-f]{64}$')
);
