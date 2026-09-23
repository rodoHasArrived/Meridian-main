-- A timestamp cutoff is an eligibility filter, not a durable commit snapshot. These immutable
-- receipts retain the exact observed result and hierarchy so replay cannot change after a late commit.
create table if not exists __SCHEMA__.security_price_selection_receipts (
    receipt_id uuid primary key,
    security_id uuid not null references __SCHEMA__.securities(security_id),
    account_id text not null,
    payload text not null,
    payload_sha256 char(64) not null,
    recorded_at timestamptz not null default now()
);
create index if not exists ix_security_price_selection_receipts_scope
    on __SCHEMA__.security_price_selection_receipts (security_id, account_id, receipt_id);
comment on table __SCHEMA__.security_price_selection_receipts is
    'Immutable golden-copy evaluation result, exact hierarchy snapshot, and quote comparisons; scoped receipt replay is the reproducibility authority.';

create function __SCHEMA__.reject_price_selection_receipt_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'Security Master price selection receipts are immutable' using errcode = '23514';
end;
$$;
create trigger security_price_selection_receipts_immutable
    before update or delete on __SCHEMA__.security_price_selection_receipts
    for each row execute function __SCHEMA__.reject_price_selection_receipt_mutation();
create trigger security_price_selection_receipts_no_truncate
    before truncate on __SCHEMA__.security_price_selection_receipts
    for each statement execute function __SCHEMA__.reject_price_selection_receipt_mutation();
