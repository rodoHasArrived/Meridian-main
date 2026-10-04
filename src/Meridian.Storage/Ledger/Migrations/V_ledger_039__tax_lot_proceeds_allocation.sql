-- Retain the reconstruction convention only on newly posted disposals. Existing batches stay
-- unversioned: assigning them the new allocator would rewrite parcel gains and tax character.
-- No default/backfill and no command-fingerprint change; the existing immutable batch trigger
-- protects both fields. A current-version NULL price means the native aggregate-only journal
-- convention, which derives proceeds after retained wash-sale deferrals. A supplied original
-- quote uses unconstrained numeric to preserve its decimal input exactly.
alter table __SCHEMA__.atomic_tax_lot_posting_batches
    add column if not exists proceeds_allocation_version integer null,
    add column if not exists disposal_sale_price numeric null;

do $migration$
begin
    if not exists (select 1 from pg_constraint
        where conrelid = '__SCHEMA__.atomic_tax_lot_posting_batches'::regclass
          and conname = 'ck_atomic_tax_lot_batch_proceeds_allocation') then
        alter table __SCHEMA__.atomic_tax_lot_posting_batches
            add constraint ck_atomic_tax_lot_batch_proceeds_allocation check (
                (proceeds_allocation_version is null and disposal_sale_price is null) or
                (mutation_kind = 'Disposal' and proceeds_allocation_version is not null
                 and proceeds_allocation_version = 1
                 and (disposal_sale_price is null or disposal_sale_price >= 0)));
    end if;
end
$migration$;
