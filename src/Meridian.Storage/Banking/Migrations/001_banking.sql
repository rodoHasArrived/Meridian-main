-- Banking schema: pending_payments and bank_transactions tables
-- Schema placeholder replaced at runtime: __SCHEMA__

CREATE SCHEMA IF NOT EXISTS __SCHEMA__;

-- ─── Pending payments (approval workflow) ───────────────────────────────────

CREATE TABLE IF NOT EXISTS __SCHEMA__.pending_payments (
    pending_payment_id  UUID            NOT NULL PRIMARY KEY,
    entity_id           UUID            NOT NULL,
    amount              NUMERIC(19,4)   NOT NULL,
    effective_date      DATE            NOT NULL,
    external_ref        TEXT,
    notes               TEXT,
    status              SMALLINT        NOT NULL DEFAULT 0,
    reviewed_by         TEXT,
    review_notes        TEXT,
    initiated_at        TIMESTAMPTZ     NOT NULL,
    reviewed_at         TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS ix_pending_payments_entity_id
    ON __SCHEMA__.pending_payments (entity_id);

CREATE INDEX IF NOT EXISTS ix_pending_payments_status
    ON __SCHEMA__.pending_payments (status);

-- ─── Bank transactions ───────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS __SCHEMA__.bank_transactions (
    bank_transaction_id UUID            NOT NULL PRIMARY KEY,
    entity_id           UUID            NOT NULL,
    transaction_type    TEXT            NOT NULL,
    effective_date      DATE            NOT NULL,
    transaction_date    DATE            NOT NULL,
    settlement_date     DATE            NOT NULL,
    amount              NUMERIC(19,4)   NOT NULL,
    currency            TEXT            NOT NULL DEFAULT 'USD',
    external_ref        TEXT,
    recorded_at         TIMESTAMPTZ     NOT NULL,
    is_voided           BOOLEAN         NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS ix_bank_transactions_entity_id
    ON __SCHEMA__.bank_transactions (entity_id);

CREATE INDEX IF NOT EXISTS ix_bank_transactions_effective_date
    ON __SCHEMA__.bank_transactions (effective_date);
