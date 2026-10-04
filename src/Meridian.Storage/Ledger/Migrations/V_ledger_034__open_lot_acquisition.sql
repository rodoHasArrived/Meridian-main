-- Additive shadow contract. No legacy row receives invented acquisition facts.
alter table __SCHEMA__.tax_lots
    add column if not exists acquisition_terms jsonb null;

do $migration$
begin
    if not exists (select 1 from pg_constraint
        where conrelid = '__SCHEMA__.tax_lots'::regclass and conname = 'ck_tax_lots_acquisition_identity') then
        alter table __SCHEMA__.tax_lots add constraint ck_tax_lots_acquisition_identity check (
            acquisition_terms is null or (
                jsonb_typeof(acquisition_terms) = 'object'
                and security_id is not null and security_id <> '00000000-0000-0000-0000-000000000000'::uuid
                and book_position_id is not null and book_position_id <> '00000000-0000-0000-0000-000000000000'::uuid));
    end if;
end
$migration$;

create or replace function __SCHEMA__.protect_open_lot_acquisition()
returns trigger language plpgsql as $function$
begin
    if old.acquisition_terms is distinct from new.acquisition_terms then
        raise exception 'Retained open-lot acquisition facts are immutable; use governed correction lineage';
    end if;
    if old.acquisition_terms is not null and (
        old.security_id is distinct from new.security_id or old.book_position_id is distinct from new.book_position_id
        or old.ledger_book_id is distinct from new.ledger_book_id or old.acquired_date is distinct from new.acquired_date
        or old.original_quantity is distinct from new.original_quantity or old.currency is distinct from new.currency
        or old.unit_cost is distinct from new.unit_cost or old.original_face is distinct from new.original_face
        or old.booked_factor is distinct from new.booked_factor or old.par_basis is distinct from new.par_basis) then
        raise exception 'Canonical lot identity and acquisition economics require append-only correction lineage';
    end if;
    return new;
end
$function$;

do $migration$
begin
    if not exists (select 1 from pg_trigger
        where tgrelid = '__SCHEMA__.tax_lots'::regclass and tgname = 'protect_open_lot_acquisition') then
        create trigger protect_open_lot_acquisition before update on __SCHEMA__.tax_lots
            for each row execute function __SCHEMA__.protect_open_lot_acquisition();
    end if;
end
$migration$;
