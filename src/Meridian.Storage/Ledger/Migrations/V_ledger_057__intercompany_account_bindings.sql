-- Stable intercompany chart identity and immutable, effective-dated mapping revisions.
alter table __SCHEMA__.accounting_configuration_chart_nodes
    add column if not exists intercompany_role text null;

create table if not exists __SCHEMA__.intercompany_account_binding_revisions (
    revision_id uuid primary key,
    binding_id uuid not null,
    revision_number bigint not null check (revision_number > 0),
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    tenant_id text not null check (length(btrim(tenant_id)) > 0 and lower(btrim(tenant_id)) <> 'all'),
    company_id text null,
    chart_account_node_id text not null,
    effective_from date not null,
    effective_to date null,
    recorded_at_utc timestamptz not null,
    payload jsonb not null,
    unique (tenant_id, ledger_book_id, binding_id, revision_number),
    check (effective_to is null or effective_to > effective_from)
);

create index if not exists ix_intercompany_account_binding_scope
    on __SCHEMA__.intercompany_account_binding_revisions(tenant_id, ledger_book_id, effective_from, revision_number);

create or replace function __SCHEMA__.refuse_intercompany_account_binding_rewrite()
returns trigger language plpgsql as $$
begin
    raise exception 'Intercompany account binding revisions are append-only; append an audited successor';
end;
$$;

drop trigger if exists tr_intercompany_account_binding_immutable on __SCHEMA__.intercompany_account_binding_revisions;
create trigger tr_intercompany_account_binding_immutable
    before update or delete on __SCHEMA__.intercompany_account_binding_revisions
    for each row execute function __SCHEMA__.refuse_intercompany_account_binding_rewrite();

drop trigger if exists tr_intercompany_account_binding_no_truncate on __SCHEMA__.intercompany_account_binding_revisions;
create trigger tr_intercompany_account_binding_no_truncate
    before truncate on __SCHEMA__.intercompany_account_binding_revisions
    for each statement execute function __SCHEMA__.refuse_intercompany_account_binding_rewrite();
