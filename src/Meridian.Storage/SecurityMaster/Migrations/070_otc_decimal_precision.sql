-- Migration: security-master / otc_decimal_precision
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Preserve full decimal values in the OTC contract, collateral, exposure and margin tables (security
-- master review, Priority 1 step 3). The numeric(28,10) typmod silently rounded amounts past ten
-- decimal places and refused integer parts above 18 digits, the defect migration 058 already removed
-- from the schedule projections. Removing the typmod widens each column without changing retained
-- values: numeric(p,s) to unconstrained numeric needs no table rewrite, so the append-only and
-- no-update triggers on the exposure-snapshot and lifecycle tables do not fire. Values already
-- rounded on write are not reconstructed by DDL.
alter table __SCHEMA__.otc_collateral_agreements
    alter column threshold_ours type numeric,
    alter column threshold_theirs type numeric,
    alter column minimum_transfer_amount type numeric,
    alter column independent_amount type numeric,
    alter column rounding_amount type numeric;

alter table __SCHEMA__.otc_contracts
    alter column notional type numeric;

alter table __SCHEMA__.otc_exposure_snapshots
    alter column net_exposure type numeric,
    alter column gross_positive type numeric,
    alter column gross_negative type numeric;

alter table __SCHEMA__.otc_collateral_balances
    alter column quantity type numeric,
    alter column unit_value type numeric,
    alter column valued_amount type numeric,
    alter column haircut_percent type numeric;

alter table __SCHEMA__.otc_margin_calls
    alter column amount_called type numeric,
    alter column amount_agreed type numeric,
    alter column settled_amount type numeric;
