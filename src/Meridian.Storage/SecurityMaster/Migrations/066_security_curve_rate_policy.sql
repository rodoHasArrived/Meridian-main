-- Curve identity fixes the admission policy; legacy definitions retain their previous domain.
ALTER TABLE __SCHEMA__.security_curves
    ADD COLUMN IF NOT EXISTS rate_policy TEXT NOT NULL DEFAULT 'LegacyNonNegative';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_security_curves_rate_policy'
        AND conrelid = '__SCHEMA__.security_curves'::regclass) THEN
        ALTER TABLE __SCHEMA__.security_curves ADD CONSTRAINT ck_security_curves_rate_policy
            CHECK (rate_policy IN ('LegacyNonNegative', 'SignedRates'));
    END IF;
END $$;
