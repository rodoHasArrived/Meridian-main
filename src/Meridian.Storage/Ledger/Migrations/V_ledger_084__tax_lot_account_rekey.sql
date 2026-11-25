-- Migration: ledger / tax_lot_account_rekey
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Security Master review F2 follow-through. V_ledger_083 keyed per-security accounts by
-- account_security_id but kept existing rows on their ticker keys, so a governed reclass that moves
-- a legacy ticker-keyed balance onto its SecurityId-keyed account left the open tax lots behind.
-- The governed AccountRekey lot mutation moves both in one atomic journal/lot batch: the balanced
-- reclass journal is appended and every open lot of the legacy account is re-keyed in place
-- (account_security_id null -> the lot's own instrument security_id). The lot keeps its record id,
-- label, acquisition date, quantity, basis, acquisition facts, lineage and wash-sale links; only its
-- account key, version and last mutation batch change. Nothing is backfilled or rewritten here.
--
-- Statements, every one re-runnable under DriftPolicy.Reapply:
--   * atomic_tax_lot_posting_batches.account_rekey_instruction (nullable jsonb), present exactly on
--     AccountRekey batches (the V050/V053 exclusivity shape).
--   * The four V053 checks are replaced by strict supersets admitting 'AccountRekey'. The mutation
--     row preserves quantity (delta 0), carries the before snapshot, and its cost_basis is the open
--     functional basis moved (>= 0). The economic tie (reclass amount = sum(cost_basis) = legacy
--     account balance) is enforced by the typed store.
--   * protect_tax_lot_account_key (new trigger): a lot's account_security_id may change only from
--     null to the lot's own instrument security_id, through a versioned AccountRekey batch of the
--     same book, security and position that leaves quantity unchanged.
--
-- The batch row is inserted before the lot compare-and-swap in the same transaction, so the trigger's
-- join on atomic_tax_lot_posting_batches resolves in-transaction (the V043 ordering).

alter table __SCHEMA__.atomic_tax_lot_posting_batches
    add column if not exists account_rekey_instruction jsonb null;
alter table __SCHEMA__.atomic_tax_lot_posting_batches drop constraint if exists ck_atomic_tax_lot_account_rekey_instruction;
alter table __SCHEMA__.atomic_tax_lot_posting_batches add constraint ck_atomic_tax_lot_account_rekey_instruction
    check ((mutation_kind = 'AccountRekey' and account_rekey_instruction is not null
        and jsonb_typeof(account_rekey_instruction) = 'object')
        or (mutation_kind <> 'AccountRekey' and account_rekey_instruction is null));

alter table __SCHEMA__.atomic_tax_lot_posting_batches drop constraint if exists ck_atomic_tax_lot_batch_kind;
alter table __SCHEMA__.atomic_tax_lot_posting_batches add constraint ck_atomic_tax_lot_batch_kind
    check (mutation_kind in ('Acquisition', 'Disposal', 'Amortization', 'CorporateAction', 'CapitalReturn', 'FactorPaydown',
        'AccountRekey'));
alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_kind;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_kind
    check (mutation_kind in ('Acquisition', 'Disposal', 'BasisRedistribution', 'Amortization', 'CorporateAction', 'CapitalReturn',
        'FactorPaydown', 'AccountRekey'));
alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_quantities;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_quantities check (
    quantity_before >= 0 and quantity_after >= 0 and quantity_after = quantity_before + quantity_delta
    and ((mutation_kind = 'Acquisition' and quantity_before = 0 and quantity_delta > 0)
        or (mutation_kind = 'Disposal' and quantity_delta < 0)
        or (mutation_kind = 'BasisRedistribution' and quantity_delta = 0 and quantity_before > 0
            and lower(coalesce(relief_method, '')) = 'averagecost')
        or (mutation_kind in ('Amortization', 'CapitalReturn') and quantity_delta = 0 and quantity_before > 0
            and relief_method is null and policy_revision is null)
        or (mutation_kind = 'FactorPaydown' and quantity_before > 0 and relief_method is null and policy_revision is null
            and lot_snapshot_before is not null and (quantity_delta = 0 or quantity_after = 0))
        or (mutation_kind = 'AccountRekey' and quantity_delta = 0 and quantity_before > 0
            and relief_method is null and policy_revision is null and lot_snapshot_before is not null)
        or (mutation_kind = 'CorporateAction' and relief_method is null and policy_revision is null
            and ((quantity_before > 0 and quantity_after = 0 and lot_snapshot_before is not null)
                or (quantity_before = 0 and quantity_after > 0 and lot_snapshot_before is null)))));
alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_cost;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_cost check (
    unit_cost >= 0 and cost_basis >= 0 and
    (cost_basis = abs(quantity_delta) * unit_cost
        or lower(coalesce(relief_method, '')) = 'averagecost'
        or (mutation_kind in ('Amortization', 'CorporateAction', 'CapitalReturn') and cost_basis > 0)
        or mutation_kind in ('FactorPaydown', 'AccountRekey')
        or (mutation_kind = 'Disposal'
            and lower(coalesce(relief_method, '')) in ('fifo', 'lifo', 'hifo', 'specificid')
            and coalesce(jsonb_typeof(lot_snapshot_before -> 'acquisition') = 'object', false)
            and coalesce(jsonb_typeof(lot_snapshot_before -> 'basisAdjustment') = 'object', false))));

create or replace function __SCHEMA__.protect_tax_lot_account_key()
returns trigger language plpgsql as $function$
begin
    if old.account_security_id is not distinct from new.account_security_id then
        return new;
    end if;
    if old.account_security_id is not null
        or new.account_security_id is null
        or new.account_security_id is distinct from old.security_id then
        raise exception 'A tax lot''s account key moves only from its legacy ticker key to its own instrument SecurityId';
    end if;
    if new.version <> old.version + 1
        or new.last_mutation_batch_id is null
        or new.last_mutation_batch_id is not distinct from old.last_mutation_batch_id
        or new.open_quantity <> old.open_quantity then
        raise exception 'A tax lot''s account key changes only through a versioned, quantity-preserving mutation batch';
    end if;
    if not exists (
        select 1
        from __SCHEMA__.atomic_tax_lot_posting_batches rekey
        where rekey.mutation_batch_id = new.last_mutation_batch_id
          and rekey.mutation_kind = 'AccountRekey'
          and rekey.ledger_book_id = old.ledger_book_id
          and rekey.security_id = old.security_id
          and rekey.book_position_id = old.book_position_id
          and rekey.account_rekey_instruction is not null) then
        raise exception 'A tax lot''s account key changes only through an AccountRekey mutation batch';
    end if;
    return new;
end
$function$;

do $migration$
begin
    if not exists (select 1 from pg_trigger
        where tgrelid = '__SCHEMA__.tax_lots'::regclass and tgname = 'protect_tax_lot_account_key') then
        create trigger protect_tax_lot_account_key before update on __SCHEMA__.tax_lots
            for each row execute function __SCHEMA__.protect_tax_lot_account_key();
    end if;
end
$migration$;
