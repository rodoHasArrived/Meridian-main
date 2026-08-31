-- Activates the wash-sale engine that Meridian.Ledger has carried but production never fed.
--
-- Three things were missing durably: an effective-dated policy per account (so enabling deferral
-- does not silently restate already-closed periods), an index that makes "find replacement
-- acquisitions of this security within +/-30 days" cheap enough to run on every loss disposal, and
-- retention of each deferral so a disallowed loss can be traced to the replacement lot whose basis
-- absorbed it.

-- Wash-sale policy rides on the existing effective-dated tax-lot policy so relief method and
-- deferral policy resolve together from one row rather than drifting apart.
alter table if exists __SCHEMA__.tax_lot_policies
    add column if not exists wash_sale_enabled boolean not null default false,
    add column if not exists wash_sale_window_days integer not null default 30,
    add column if not exists wash_sale_scope text not null default 'LedgerBook',
    add column if not exists wash_sale_effective_date date null;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conrelid = '__SCHEMA__.tax_lot_policies'::regclass
          and conname = 'ck_tax_lot_policies_wash_sale') then
        alter table __SCHEMA__.tax_lot_policies
            add constraint ck_tax_lot_policies_wash_sale check (
                wash_sale_window_days >= 0 and
                wash_sale_scope in ('DisposingAccount', 'LedgerBook'));
    end if;
end $$;

-- A replacement lot's increased basis and inherited holding period (IRC 1091(d)/1223(3)) are NOT
-- denormalized onto tax_lots. wash_sale_deferrals below is the single source of truth: the relief
-- engine replays retained deferrals as basis adjustments when the replacement is later relieved, so
-- a lot's effective basis and holding-period start are always derived from the deferrals that
-- produced them rather than from a copy that can drift out of step with a correction.

-- The replacement search runs once per loss-generating disposal and is keyed by book + security +
-- acquisition window. ix_tax_lots_asset_scope_open cannot serve it: it is partial on open quantity
-- (a fully-disposed lot is still a valid replacement) and orders book_position_id ahead of
-- acquired_date, which a book-wide scope does not constrain.
create index if not exists ix_tax_lots_book_security_acquired
    on __SCHEMA__.tax_lots (ledger_book_id, security_id, acquired_date, lot_id);

-- One row per (disposal, replacement lot) pair. Retaining the deferral is what lets a later sale of
-- the replacement relieve it at its increased basis, and what lets the report pack show which lot
-- absorbed the loss instead of only that some amount was disallowed.
create table if not exists __SCHEMA__.wash_sale_deferrals (
    deferral_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id) on delete cascade,
    disposal_mutation_batch_id uuid not null
        references __SCHEMA__.atomic_tax_lot_posting_batches(mutation_batch_id),
    security_id uuid not null,
    sale_date date not null,
    disposal_account_name text not null,
    disposal_account_type text not null,
    disposal_symbol text null,
    disposal_financial_account_id text null,
    replacement_tax_lot_record_id uuid not null references __SCHEMA__.tax_lots(tax_lot_record_id),
    replacement_lot_id text not null,
    disallowed_amount numeric(38, 12) not null,
    matched_quantity numeric(38, 12) not null,
    holding_period_carry_date date not null,
    policy_id text not null,
    window_days integer not null,
    scope text not null,
    recorded_at timestamptz not null,
    constraint ck_wash_sale_deferrals_amount
        check (disallowed_amount > 0),
    constraint ck_wash_sale_deferrals_matched_quantity
        check (matched_quantity > 0),
    constraint ck_wash_sale_deferrals_security
        check (security_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    constraint ck_wash_sale_deferrals_window
        check (window_days >= 0),
    constraint ck_wash_sale_deferrals_scope
        check (scope in ('DisposingAccount', 'LedgerBook')),
    constraint ck_wash_sale_deferrals_lot
        check (length(trim(replacement_lot_id)) > 0)
);

-- A disposal defers at most once into any single replacement lot, so a replayed or retried batch
-- cannot double-capitalize the same loss.
create unique index if not exists ux_wash_sale_deferrals_batch_replacement
    on __SCHEMA__.wash_sale_deferrals (disposal_mutation_batch_id, replacement_tax_lot_record_id);

-- Serves the "what has already been capitalized into the lots I am about to relieve?" lookup.
create index if not exists ix_wash_sale_deferrals_replacement
    on __SCHEMA__.wash_sale_deferrals (ledger_book_id, replacement_tax_lot_record_id);

-- Serves report-pack retrieval for a period.
create index if not exists ix_wash_sale_deferrals_book_sale_date
    on __SCHEMA__.wash_sale_deferrals (ledger_book_id, sale_date);
