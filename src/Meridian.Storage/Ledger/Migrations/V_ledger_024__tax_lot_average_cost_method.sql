-- Permit the AverageCost tax-lot relief method in persisted policies.
--
-- V_ledger_009 created tax_lot_policies with a relief_method check constraint limited to
-- Fifo/Lifo/Hifo/SpecificId. The ledger engine now also supports LedgerTaxLotReliefMethod.AverageCost
-- (pooled average cost), which is persisted as the enum name "AverageCost" via ReliefMethod.ToString().
-- Without widening the constraint, saving an AverageCost policy through the Postgres ledger store would
-- violate ck_tax_lot_policies_relief_method, making the method unusable for durable ledger books.
--
-- The migration runner replays every script on each startup (there is no version table), so this is
-- written to be idempotent: the constraint is dropped if present and re-added with the widened value set.
-- Existing rows (Fifo/Lifo/Hifo/SpecificId) already satisfy the widened constraint, so the ADD never
-- fails on legacy data.

alter table if exists __SCHEMA__.tax_lot_policies
    drop constraint if exists ck_tax_lot_policies_relief_method;

alter table if exists __SCHEMA__.tax_lot_policies
    add constraint ck_tax_lot_policies_relief_method
        check (relief_method in ('Fifo', 'Lifo', 'Hifo', 'SpecificId', 'AverageCost'));
