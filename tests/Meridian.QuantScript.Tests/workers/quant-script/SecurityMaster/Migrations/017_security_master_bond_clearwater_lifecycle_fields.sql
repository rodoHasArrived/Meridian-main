-- Clearwater extended bond lifecycle fields.
-- Adds par, payment frequency, subclass, and the extended option/redemption dates
-- (legal final maturity, pre-refund, mandatory put) used by BondLifecycleDto.
-- Subclass is denormalised here alongside bond_projection so both the joined
-- reference query and the standalone lifecycle query resolve it from one table.
alter table __SCHEMA__.bond_lifecycle_projection
    add column if not exists subclass text null,
    add column if not exists par numeric null,
    add column if not exists payment_frequency text null,
    add column if not exists legal_final_maturity date null,
    add column if not exists pre_refund_date date null,
    add column if not exists mandatory_put_date date null;
