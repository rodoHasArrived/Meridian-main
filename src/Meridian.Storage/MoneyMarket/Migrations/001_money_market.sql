-- Money Market Fund schema: mmf_funds, mmf_liquidity_overrides, mmf_rebuild_checkpoints
-- Schema placeholder replaced at runtime: __SCHEMA__

CREATE SCHEMA IF NOT EXISTS __SCHEMA__;

-- ─── MMF fund records ────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS __SCHEMA__.mmf_funds (
    security_id                 UUID            NOT NULL PRIMARY KEY,
    display_name                TEXT            NOT NULL,
    currency                    TEXT            NOT NULL DEFAULT 'USD',
    fund_family                 TEXT,
    is_sweep_eligible           BOOLEAN         NOT NULL DEFAULT FALSE,
    weighted_average_maturity   INTEGER,
    has_liquidity_fee           BOOLEAN         NOT NULL DEFAULT FALSE,
    is_active                   BOOLEAN         NOT NULL DEFAULT TRUE,
    effective_from              TIMESTAMPTZ     NOT NULL,
    effective_to                TIMESTAMPTZ,
    version                     BIGINT          NOT NULL DEFAULT 1
);

CREATE INDEX IF NOT EXISTS ix_mmf_funds_fund_family
    ON __SCHEMA__.mmf_funds (fund_family)
    WHERE fund_family IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_mmf_funds_is_active
    ON __SCHEMA__.mmf_funds (is_active);

-- ─── Per-fund liquidity state overrides ─────────────────────────────────────

CREATE TABLE IF NOT EXISTS __SCHEMA__.mmf_liquidity_overrides (
    security_id     UUID        NOT NULL PRIMARY KEY,
    liquidity_state SMALLINT    NOT NULL,
    set_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ─── Rebuild checkpoints ─────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS __SCHEMA__.mmf_rebuild_checkpoints (
    security_id         UUID        NOT NULL PRIMARY KEY,
    aggregate_version   BIGINT      NOT NULL,
    checkpointed_at     TIMESTAMPTZ NOT NULL,
    rebuild_source      TEXT        NOT NULL
);
