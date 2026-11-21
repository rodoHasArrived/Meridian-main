-- Coupon recognition and cash-consumption receipts commit atomically with their journal.
create table if not exists __SCHEMA__.bond_coupon_recognition (
    receipt_id text primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    account_id uuid not null,
    tax_lot_record_id uuid not null references __SCHEMA__.tax_lots(tax_lot_record_id),
    run_id text not null,
    previous_receipt_id text references __SCHEMA__.bond_coupon_recognition(receipt_id),
    reverses_receipt_id text unique references __SCHEMA__.bond_coupon_recognition(receipt_id),
    journal_entry_id uuid not null references __SCHEMA__.journal_entries(journal_entry_id) deferrable initially deferred,
    posted_at timestamptz not null,
    payload text not null,
    unique(ledger_book_id, account_id, tax_lot_record_id, run_id)
);
create unique index if not exists ux_bond_coupon_recognition_parent
    on __SCHEMA__.bond_coupon_recognition(ledger_book_id, account_id, tax_lot_record_id, coalesce(previous_receipt_id, ''));
create table if not exists __SCHEMA__.bond_coupon_cash_consumption (
    ledger_book_id uuid not null,
    account_id uuid not null,
    payment_version_id text not null,
    payment_flow_id text not null,
    tax_lot_record_id uuid not null references __SCHEMA__.tax_lots(tax_lot_record_id),
    expected_flow_id text not null,
    allocation_id text not null,
    receipt_id text not null references __SCHEMA__.bond_coupon_recognition(receipt_id),
    net_amount numeric(38,12) not null,
    gross_amount numeric(38,12) not null,
    withholding_tax numeric(38,12) not null,
    fees numeric(38,12) not null,
    primary key(ledger_book_id, account_id, allocation_id, receipt_id)
);
create index if not exists ix_bond_coupon_cash_flow
    on __SCHEMA__.bond_coupon_cash_consumption(ledger_book_id,account_id,payment_flow_id);
create index if not exists ix_bond_coupon_cash_obligation
    on __SCHEMA__.bond_coupon_cash_consumption(ledger_book_id,account_id,tax_lot_record_id,expected_flow_id);

create or replace function __SCHEMA__.reject_bond_coupon_receipt_rewrite() returns trigger
language plpgsql as $$ begin
    raise exception 'Posted coupon recognition and cash consumption are immutable' using errcode='55000';
end $$;
create or replace trigger bond_coupon_recognition_immutable
    before update or delete on __SCHEMA__.bond_coupon_recognition
    for each row execute function __SCHEMA__.reject_bond_coupon_receipt_rewrite();
create or replace trigger bond_coupon_consumption_immutable
    before update or delete on __SCHEMA__.bond_coupon_cash_consumption
    for each row execute function __SCHEMA__.reject_bond_coupon_receipt_rewrite();
