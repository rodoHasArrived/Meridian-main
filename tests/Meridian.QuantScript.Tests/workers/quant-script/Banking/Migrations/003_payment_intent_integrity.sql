-- Bind approved payment intents to explicit currency and replay-safe bank evidence.
-- Existing pending-payment rows intentionally remain NULL: no historical currency can be
-- inferred safely, and application services block those legacy intents until remediated.
-- Schema placeholder replaced at runtime: __SCHEMA__

ALTER TABLE __SCHEMA__.pending_payments
    ADD COLUMN IF NOT EXISTS currency TEXT,
    ADD COLUMN IF NOT EXISTS currency_remediated_by TEXT,
    ADD COLUMN IF NOT EXISTS currency_remediation_reason TEXT,
    ADD COLUMN IF NOT EXISTS currency_remediated_at TIMESTAMPTZ;

ALTER TABLE __SCHEMA__.bank_transactions
    ADD COLUMN IF NOT EXISTS pending_payment_id UUID,
    ADD COLUMN IF NOT EXISTS evidence_id TEXT,
    ADD COLUMN IF NOT EXISTS canonical_input_hash TEXT;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_pending_payments_currency'
          AND conrelid = '__SCHEMA__.pending_payments'::regclass
    ) THEN
        ALTER TABLE __SCHEMA__.pending_payments
            ADD CONSTRAINT ck_pending_payments_currency
                CHECK (currency IS NULL OR currency ~ '^[A-Z]{3}$') NOT VALID;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_pending_payments_currency_remediation'
          AND conrelid = '__SCHEMA__.pending_payments'::regclass
    ) THEN
        ALTER TABLE __SCHEMA__.pending_payments
            ADD CONSTRAINT ck_pending_payments_currency_remediation
                CHECK (
                    (currency_remediated_by IS NULL
                        AND currency_remediation_reason IS NULL
                        AND currency_remediated_at IS NULL)
                    OR
                    (currency IS NOT NULL
                        AND currency_remediated_by IS NOT NULL
                        AND btrim(currency_remediated_by) <> ''
                        AND currency_remediation_reason IS NOT NULL
                        AND btrim(currency_remediation_reason) <> ''
                        AND currency_remediated_at IS NOT NULL)
                ) NOT VALID;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_bank_transactions_currency'
          AND conrelid = '__SCHEMA__.bank_transactions'::regclass
    ) THEN
        ALTER TABLE __SCHEMA__.bank_transactions
            ADD CONSTRAINT ck_bank_transactions_currency
                CHECK (currency ~ '^[A-Z]{3}$') NOT VALID;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_bank_transactions_payment_evidence'
          AND conrelid = '__SCHEMA__.bank_transactions'::regclass
    ) THEN
        ALTER TABLE __SCHEMA__.bank_transactions
            ADD CONSTRAINT ck_bank_transactions_payment_evidence
                CHECK (
                    (pending_payment_id IS NULL
                        AND evidence_id IS NULL
                        AND canonical_input_hash IS NULL)
                    OR
                    (pending_payment_id IS NOT NULL
                        AND evidence_id IS NOT NULL
                        AND btrim(evidence_id) <> ''
                        AND length(evidence_id) <= 200
                        AND canonical_input_hash ~ '^[0-9A-F]{64}$')
                ) NOT VALID;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_bank_transactions_pending_payment'
          AND conrelid = '__SCHEMA__.bank_transactions'::regclass
    ) THEN
        ALTER TABLE __SCHEMA__.bank_transactions
            ADD CONSTRAINT fk_bank_transactions_pending_payment
                FOREIGN KEY (pending_payment_id)
                REFERENCES __SCHEMA__.pending_payments (pending_payment_id)
                ON DELETE RESTRICT
                NOT VALID;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_bank_transactions_payment_evidence
    ON __SCHEMA__.bank_transactions (pending_payment_id, evidence_id);

CREATE INDEX IF NOT EXISTS ix_bank_transactions_pending_payment_id
    ON __SCHEMA__.bank_transactions (pending_payment_id)
    WHERE pending_payment_id IS NOT NULL;
