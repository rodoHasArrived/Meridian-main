-- Existing books retain compatibility warnings; books created after migration are enforced.
create table if not exists __SCHEMA__.ledger_book_controls (
    ledger_book_id uuid primary key references __SCHEMA__.ledger_books(ledger_book_id),
    mode text not null check (mode in ('LegacyWarning','Enforced')) default 'Enforced',
    version bigint not null default 1 check (version > 0),
    policy_version text not null default 'entity-intercompany-v1',
    created_at_utc timestamptz not null default now(),
    activated_at_utc timestamptz null,
    activated_by text null,
    migration_receipt_id text null
);
insert into __SCHEMA__.ledger_book_controls(ledger_book_id, mode)
select ledger_book_id, 'LegacyWarning' from __SCHEMA__.ledger_books
on conflict (ledger_book_id) do nothing;

create or replace function __SCHEMA__.initialize_ledger_book_controls()
returns trigger language plpgsql as $$ begin
    insert into __SCHEMA__.ledger_book_controls(ledger_book_id, mode)
    values (new.ledger_book_id, 'Enforced') on conflict (ledger_book_id) do nothing;
    return new;
end; $$;
create or replace trigger initialize_ledger_book_controls after insert
on __SCHEMA__.ledger_books for each row execute function __SCHEMA__.initialize_ledger_book_controls();

create table if not exists __SCHEMA__.ledger_book_control_activation (
    receipt_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    prior_version bigint not null,
    preflight_digest text not null,
    actor text not null,
    reason text not null,
    evidence_reference text not null,
    recorded_at_utc timestamptz not null default now()
);
create or replace function __SCHEMA__.reject_control_activation_rewrite()
returns trigger language plpgsql as $$ begin
    raise exception 'Ledger book control activation receipts are immutable';
end; $$;
create or replace trigger control_activation_immutable before update or delete
on __SCHEMA__.ledger_book_control_activation for each row execute function __SCHEMA__.reject_control_activation_rewrite();
create or replace trigger control_activation_no_truncate before truncate
on __SCHEMA__.ledger_book_control_activation for each statement execute function __SCHEMA__.reject_control_activation_rewrite();

create table if not exists __SCHEMA__.entity_controlled_close_authorization (
    authorization_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    journal_entry_id uuid not null,
    draft_version integer not null,
    tenant_id text null,
    payload jsonb not null,
    recorded_at_utc timestamptz not null default now()
);
create index if not exists ix_controlled_close_draft on __SCHEMA__.entity_controlled_close_authorization
    (ledger_book_id,journal_entry_id,draft_version,recorded_at_utc desc);
create or replace trigger controlled_close_immutable before update or delete
on __SCHEMA__.entity_controlled_close_authorization for each row execute function __SCHEMA__.reject_control_activation_rewrite();
create or replace trigger controlled_close_no_truncate before truncate
on __SCHEMA__.entity_controlled_close_authorization for each statement execute function __SCHEMA__.reject_control_activation_rewrite();
