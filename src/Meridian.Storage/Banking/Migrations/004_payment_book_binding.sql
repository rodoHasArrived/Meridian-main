-- Bind payment intents to the tenant, company and ledger book they are released from, so approval
-- can refuse a shadow book that is not yet the book of record (W11-SHADOW-001).
-- Existing rows intentionally remain unbound (all three NULL): no historical book can be inferred
-- safely from an opaque entity id.
-- Schema placeholder replaced at runtime: __SCHEMA__

ALTER TABLE __SCHEMA__.pending_payments
    ADD COLUMN IF NOT EXISTS tenant_id TEXT,
    ADD COLUMN IF NOT EXISTS company_id TEXT,
    ADD COLUMN IF NOT EXISTS ledger_book_id UUID;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_pending_payments_book_binding'
          AND conrelid = '__SCHEMA__.pending_payments'::regclass
    ) THEN
        ALTER TABLE __SCHEMA__.pending_payments
            ADD CONSTRAINT ck_pending_payments_book_binding
                CHECK (
                    (tenant_id IS NULL AND company_id IS NULL AND ledger_book_id IS NULL)
                    OR
                    (tenant_id IS NOT NULL AND btrim(tenant_id) <> ''
                        AND company_id IS NOT NULL AND btrim(company_id) <> ''
                        AND ledger_book_id IS NOT NULL)
                ) NOT VALID;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_pending_payments_ledger_book_id
    ON __SCHEMA__.pending_payments (ledger_book_id)
    WHERE ledger_book_id IS NOT NULL;
