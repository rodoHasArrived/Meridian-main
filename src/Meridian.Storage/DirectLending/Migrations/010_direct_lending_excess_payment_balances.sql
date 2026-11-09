-- Migration: direct-lending / direct_lending_excess_payment_balances
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- PRD-115 increment 2: servicing balances owed to the borrower. PrepaidInterest is credited when
-- a payment's excess is applied as prepaid interest and is consumed by later daily accruals;
-- UnappliedCash holds excess that has not been applied. Neither is a receivable.
--
-- Additive only: defaulted columns, so the migration is re-runnable under the reapply drift policy,
-- existing rows read as 0, and older store versions can still start against it. Precision matches
-- interest_accrued_unpaid, which prepaid interest offsets.
-- Design: docs/engineering/blueprints/direct-lending-excess-payment-application.md.

alter table __SCHEMA__.loan_servicing_projection
    add column if not exists prepaid_interest numeric(24,2) not null default 0;

alter table __SCHEMA__.loan_servicing_projection
    add column if not exists unapplied_cash numeric(24,2) not null default 0;
